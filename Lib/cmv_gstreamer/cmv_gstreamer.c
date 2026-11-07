#include "cmv_gstreamer.h"

#include <gst/gst.h>
#include <gst/app/gstappsink.h>
#include <gst/video/video.h>
#include <math.h>
#include <string.h>

// From https://gstreamer.freedesktop.org/documentation/playback/playsink.html#named-constants
#define PLAY_VIDEO (1u << 0)
#define PLAY_AUDIO (1u << 1)
#define PLAY_SOFT_VOLUME (1u << 4)
#define PLAY_SOFTWARE_DECODERS (1u << 12)
#define CONTROL_POLL_US 5000

struct cmv_gst_player {
    GMutex lock;
    GCond wake;
    GThread *worker;
    gboolean stop;
    char *path;
    int width, height, audio_mode;
    gsize row_bytes;
    guint8 *pixels;
    char *error;
    cmv_gst_snapshot snapshot;
    cmv_gst_frame frame;
    gint64 requested_ns, position_ns;
    gboolean desired_playing, muted, at_eos;
    double rate, volume, panning;
    guint64 generation, completed_generation;
    guint32 expected_seqnum, segment_seqnum;
    guint64 expected_generation, segment_generation, preroll_generation;
    guint64 eos_generation;
    gboolean allow_publish, have_video_info;
    GstVideoInfo video_info;
    GstElement *pipeline;
    GstElement *audio_filter;
    GstAppSink *sink;
    GstPad *sink_pad;
    gulong probe_id, setup_handler;
    GstBus *bus;
};

G_LOCK_DEFINE_STATIC(initialization);
static char *private_plugins, *private_registry, *private_scanner;
static char *initialization_error;
static gboolean initialization_started, initialization_ok;
static GQuark buffer_seqnum_key, vp8_chained_key;

static void copy_error(char *out, uint64_t capacity, const char *message)
{
    if (out && capacity) g_strlcpy(out, message ? message : "", (gsize)MIN(capacity, G_MAXSIZE));
}

int32_t cmv_gst_configure(const char *plugins, const char *registry,
    const char *scanner, char *error, uint64_t capacity)
{
    if (!plugins || !g_path_is_absolute(plugins) || !registry || !g_path_is_absolute(registry)
        || (scanner && *scanner && !g_path_is_absolute(scanner))) {
        copy_error(error, capacity, "GStreamer plugin, registry and scanner paths must be absolute.");
        return 0;
    }
    G_LOCK(initialization);
    if (initialization_started) {
        gboolean same = g_strcmp0(private_plugins, plugins) == 0 && g_strcmp0(private_registry, registry) == 0
            && g_strcmp0(private_scanner, scanner && *scanner ? scanner : NULL) == 0;
        G_UNLOCK(initialization);
        if (same) { copy_error(error, capacity, NULL); return 1; }
        copy_error(error, capacity, "GStreamer initialization has already started.");
        return 0;
    }
    g_free(private_plugins); g_free(private_registry); g_free(private_scanner);
    private_plugins = g_strdup(plugins); private_registry = g_strdup(registry);
    private_scanner = scanner && *scanner ? g_strdup(scanner) : NULL;
    G_UNLOCK(initialization);
    copy_error(error, capacity, NULL);
    return 1;
}

static gboolean initialize_gstreamer(char **error)
{
    static const char *keys[] = {"GST_PLUGIN_PATH_1_0", "GST_PLUGIN_PATH",
        "GST_PLUGIN_SYSTEM_PATH_1_0", "GST_PLUGIN_SYSTEM_PATH", "GST_REGISTRY_1_0",
        "GST_REGISTRY", "GST_PLUGIN_SCANNER_1_0", "GST_PLUGIN_SCANNER", "GST_REGISTRY_FORK"};
    G_LOCK(initialization);
    if (!initialization_started) {
        initialization_started = TRUE;
        if (!private_plugins || !private_registry) {
            initialization_error = g_strdup("Call cmv_gst_configure before creating a player.");
        } else if (gst_is_initialized()) {
            initialization_error = g_strdup("GStreamer was initialized before its private paths were configured.");
        } else {
            char *saved[G_N_ELEMENTS(keys)];
#ifndef SYSTEM_GST
            for (guint i = 0; i < G_N_ELEMENTS(keys); ++i) saved[i] = g_strdup(g_getenv(keys[i]));
            g_setenv(keys[0], private_plugins, TRUE); g_setenv(keys[1], private_plugins, TRUE);
            g_setenv(keys[2], "", TRUE); g_setenv(keys[3], "", TRUE);
            g_setenv(keys[4], private_registry, TRUE); g_setenv(keys[5], private_registry, TRUE);
            if (private_scanner) {
                g_setenv(keys[6], private_scanner, TRUE); g_setenv(keys[7], private_scanner, TRUE);
            } else {
                g_setenv(keys[8], "no", TRUE);
            }
#endif
            GError *gst_error = NULL;
            initialization_ok = gst_init_check(NULL, NULL, &gst_error);
            if (!initialization_ok) {
                initialization_error = g_strdup(gst_error ? gst_error->message : "GStreamer initialization failed.");
                g_clear_error(&gst_error);
            }
            for (guint i = 0; i < G_N_ELEMENTS(keys); ++i) {
                if (saved[i]) g_setenv(keys[i], saved[i], TRUE); else g_unsetenv(keys[i]);
                g_free(saved[i]);
            }
            buffer_seqnum_key = g_quark_from_static_string("cmv-gst-buffer-seek-seqnum");
            vp8_chained_key = g_quark_from_static_string("cmv-vp8-timing-chained");
        }
    }
    if (!initialization_ok) *error = g_strdup(initialization_error);
    G_UNLOCK(initialization);
    return initialization_ok;
}

static void fail_locked(cmv_gst_player *p, const char *message)
{
    if (!p->error) p->error = g_strdup(message);
    p->snapshot.state = CMV_GST_ERROR;
    p->snapshot.error_code = 1;
    p->allow_publish = FALSE;
    g_cond_signal(&p->wake);
}

static void fail(cmv_gst_player *p, const char *message)
{
    g_mutex_lock(&p->lock); fail_locked(p, message); g_mutex_unlock(&p->lock);
}

/* SEGMENT events and buffers are serialized at this pad. Tag the buffer before
 * appsink can queue it, so a delayed callback cannot acquire a newer generation. */
static GstPadProbeReturn sink_probe(GstPad *pad, GstPadProbeInfo *info, gpointer user_data)
{
    (void)pad;
    cmv_gst_player *p = user_data;
    g_mutex_lock(&p->lock);
    if (GST_PAD_PROBE_INFO_TYPE(info) & GST_PAD_PROBE_TYPE_BUFFER) {
        guint32 seqnum = p->segment_seqnum == p->expected_seqnum
            && p->segment_generation == p->generation ? p->segment_seqnum : 0;
        gst_mini_object_set_qdata(GST_MINI_OBJECT(GST_PAD_PROBE_INFO_BUFFER(info)),
            buffer_seqnum_key, GUINT_TO_POINTER(seqnum), NULL);
    } else if (GST_PAD_PROBE_INFO_TYPE(info) & GST_PAD_PROBE_TYPE_EVENT_DOWNSTREAM) {
        GstEvent *event = GST_PAD_PROBE_INFO_EVENT(info);
        switch (GST_EVENT_TYPE(event)) {
        case GST_EVENT_FLUSH_START:
            p->segment_seqnum = 0; p->segment_generation = 0; p->eos_generation = 0;
            break;
        case GST_EVENT_SEGMENT:
            p->segment_seqnum = gst_event_get_seqnum(event);
            p->segment_generation = p->segment_seqnum == p->expected_seqnum ? p->expected_generation : 0;
            break;
        case GST_EVENT_CAPS: {
            GstCaps *caps;
            gst_event_parse_caps(event, &caps);
            p->have_video_info = gst_video_info_from_caps(&p->video_info, caps);
            if (!p->have_video_info || GST_VIDEO_INFO_FORMAT(&p->video_info) != GST_VIDEO_FORMAT_RGBA) {
                fail_locked(p, "Video sink did not negotiate the authored coded RGBA dimensions.");
            }
            p->width = GST_VIDEO_INFO_WIDTH(&p->video_info);
            p->height = GST_VIDEO_INFO_HEIGHT(&p->video_info);
            p->row_bytes = (gsize)p->width * 4;
            p->pixels = g_try_malloc(p->row_bytes * p->height);
            if (!p->pixels) { fail_locked(p, "Unable to allocate the RGBA video frame."); }
            break;
        }
        case GST_EVENT_EOS:
            p->eos_generation = p->segment_generation;
            break;
        default: break;
        }
    }
    g_mutex_unlock(&p->lock);
    return GST_PAD_PROBE_OK;
}

static GstFlowReturn publish_sample(cmv_gst_player *p, GstSample *sample, gboolean preroll)
{
    if (!sample) return GST_FLOW_FLUSHING;
    GstBuffer *buffer = gst_sample_get_buffer(sample);
    guint32 seqnum = GPOINTER_TO_UINT(gst_mini_object_get_qdata(GST_MINI_OBJECT(buffer), buffer_seqnum_key));
    g_mutex_lock(&p->lock);
    if (!p->stop && !p->error && p->allow_publish && p->have_video_info && seqnum
        && seqnum == p->expected_seqnum && p->expected_generation == p->generation) {
        GstVideoFrame mapped;
        if (!gst_video_frame_map(&mapped, &p->video_info, buffer, GST_MAP_READ)) {
            fail_locked(p, "Unable to map a decoded RGBA video frame.");
        } else {
            const guint8 *source = GST_VIDEO_FRAME_PLANE_DATA(&mapped, 0);
            gint stride = GST_VIDEO_FRAME_PLANE_STRIDE(&mapped, 0);
            for (int y = 0; y < p->height; ++y)
                memcpy(p->pixels + (gsize)(p->height - 1 - y) * p->row_bytes,
                    source + (gssize)y * stride, p->row_bytes);
            gst_video_frame_unmap(&mapped);
            const GstSegment *segment = gst_sample_get_segment(sample);
            GstClockTime stream_time = gst_segment_to_stream_time(segment, GST_FORMAT_TIME, GST_BUFFER_PTS(buffer));
            p->frame.serial++;
            p->frame.generation = p->generation;
            p->frame.raw_pts_ns = GST_BUFFER_PTS_IS_VALID(buffer) ? (gint64)GST_BUFFER_PTS(buffer) : -1;
            p->frame.stream_time_ns = GST_CLOCK_TIME_IS_VALID(stream_time) ? (gint64)stream_time : -1;
            p->frame.duration_ns = GST_BUFFER_DURATION_IS_VALID(buffer) ? (gint64)GST_BUFFER_DURATION(buffer) : -1;
            p->frame.width = p->width; p->frame.height = p->height;
            if (preroll) p->preroll_generation = p->generation;
            g_cond_signal(&p->wake);
        }
    }
    g_mutex_unlock(&p->lock);
    gst_sample_unref(sample);
    return GST_FLOW_OK;
}

static GstFlowReturn new_preroll(GstAppSink *sink, gpointer user_data)
{
    return publish_sample(user_data, gst_app_sink_pull_preroll(sink), TRUE);
}

static GstFlowReturn new_sample(GstAppSink *sink, gpointer user_data)
{
    return publish_sample(user_data, gst_app_sink_pull_sample(sink), FALSE);
}


/* WebM timestamps are quantized, while DefaultDuration may overlap the next
 * visible frame. Derive its interval before decoder clipping hides the original
 * timestamp; retain hidden packets in decode order until that next frame. */
typedef struct {
    GMutex lock;
    GQueue pending;
    GstBuffer *visible;
    guint64 epoch;
} Vp8Timing;


static gboolean vp8_visible(GstBuffer *buffer)
{
    guint8 tag;
    return !GST_BUFFER_FLAG_IS_SET(buffer, GST_BUFFER_FLAG_DECODE_ONLY)
        && (gst_buffer_extract(buffer, 0, &tag, 1) != 1 || (tag & 0x10));
}

static void vp8_timing_clear_locked(Vp8Timing *timing)
{
    g_queue_clear_full(&timing->pending, (GDestroyNotify)gst_buffer_unref);
    timing->visible = NULL;
    timing->epoch++;
}

static void vp8_timing_free(gpointer data)
{
    Vp8Timing *timing = data;
    vp8_timing_clear_locked(timing);
    g_mutex_clear(&timing->lock);
    g_free(timing);
}

static GstFlowReturn vp8_chain_pending(GstPad *pad, Vp8Timing *timing,
    GQueue *ready, guint64 epoch)
{
    GstFlowReturn flow = GST_FLOW_OK;
    GstBuffer *buffer;
    while ((buffer = g_queue_pop_head(ready))) {
        g_mutex_lock(&timing->lock);
        gboolean current = epoch == timing->epoch;
        g_mutex_unlock(&timing->lock);
        if (current && flow == GST_FLOW_OK) {
            gst_mini_object_set_qdata(GST_MINI_OBJECT(buffer), vp8_chained_key, pad, NULL);
            flow = gst_pad_chain(pad, buffer);
        } else {
            gst_buffer_unref(buffer);
            if (!current) flow = GST_FLOW_FLUSHING;
        }
    }
    return flow;
}

static GstPadProbeReturn vp8_timing_probe(GstPad *pad, GstPadProbeInfo *info, gpointer data)
{
    Vp8Timing *timing = data;
    GQueue ready = G_QUEUE_INIT;
    guint64 epoch;
    if (GST_PAD_PROBE_INFO_TYPE(info) & GST_PAD_PROBE_TYPE_BUFFER) {
        GstBuffer *buffer = GST_PAD_PROBE_INFO_BUFFER(info);
        if (gst_mini_object_get_qdata(GST_MINI_OBJECT(buffer), vp8_chained_key) == pad) {
            gst_mini_object_set_qdata(GST_MINI_OBJECT(buffer), vp8_chained_key, NULL, NULL);
            return GST_PAD_PROBE_OK;
        }
        g_mutex_lock(&timing->lock);
        if (vp8_visible(buffer)) {
            if (timing->visible && GST_BUFFER_PTS_IS_VALID(timing->visible)
                && GST_BUFFER_PTS_IS_VALID(buffer)
                && GST_BUFFER_PTS(buffer) >= GST_BUFFER_PTS(timing->visible)) {
                GstBuffer *previous = gst_buffer_make_writable(timing->visible);
                timing->pending.head->data = previous;
                GST_BUFFER_DURATION(previous) = GST_BUFFER_PTS(buffer) - GST_BUFFER_PTS(previous);
            }
            ready = timing->pending;
            g_queue_init(&timing->pending);
            timing->visible = buffer;
        }
        g_queue_push_tail(&timing->pending, gst_buffer_ref(buffer));
        epoch = timing->epoch;
        g_mutex_unlock(&timing->lock);
        GstFlowReturn flow = vp8_chain_pending(pad, timing, &ready, epoch);
        if (flow != GST_FLOW_OK) {
            GST_PAD_PROBE_INFO_FLOW_RETURN(info) = flow;
            gst_buffer_unref(buffer);
            GST_PAD_PROBE_INFO_DATA(info) = NULL;
            return GST_PAD_PROBE_HANDLED;
        }
        return GST_PAD_PROBE_DROP;
    }
    if (GST_PAD_PROBE_INFO_TYPE(info) & GST_PAD_PROBE_TYPE_EVENT_DOWNSTREAM) {
        GstEvent *event = GST_PAD_PROBE_INFO_EVENT(info);
        g_mutex_lock(&timing->lock);
        if (GST_EVENT_TYPE(event) == GST_EVENT_FLUSH_START
            || GST_EVENT_TYPE(event) == GST_EVENT_FLUSH_STOP
            || GST_EVENT_TYPE(event) == GST_EVENT_SEGMENT) {
            vp8_timing_clear_locked(timing);
        } else if (GST_EVENT_TYPE(event) == GST_EVENT_EOS) {
            ready = timing->pending;
            g_queue_init(&timing->pending);
            timing->visible = NULL;
        }
        epoch = timing->epoch;
        g_mutex_unlock(&timing->lock);
        vp8_chain_pending(pad, timing, &ready, epoch);
    }
    return GST_PAD_PROBE_OK;
}

static void vp8_attach_timing(GstElement *element)
{
    Vp8Timing *timing = g_new0(Vp8Timing, 1);
    g_mutex_init(&timing->lock);
    g_queue_init(&timing->pending);
    GstPad *pad = gst_element_get_static_pad(element, "sink");
    gst_pad_add_probe(pad, GST_PAD_PROBE_TYPE_BUFFER
        | GST_PAD_PROBE_TYPE_EVENT_DOWNSTREAM | GST_PAD_PROBE_TYPE_EVENT_FLUSH,
        vp8_timing_probe, timing, vp8_timing_free);
    gst_object_unref(pad);
}

static void element_setup(GstElement *playbin, GstElement *element, gpointer user_data)
{
    (void)playbin; (void)user_data;
    GstElementFactory *factory = gst_element_get_factory(element);
    if (factory && g_str_equal(gst_plugin_feature_get_name(GST_PLUGIN_FEATURE(factory)), "avdec_vp8"))
        vp8_attach_timing(element);
    if (factory && g_str_equal(gst_plugin_feature_get_name(GST_PLUGIN_FEATURE(factory)), "avdec_h264"))
        g_object_set(element, "max-threads", 2, NULL);
}

static gboolean build_pipeline(cmv_gst_player *p)
{
    char *error = NULL;
    if (!initialize_gstreamer(&error)) { fail(p, error); g_free(error); return FALSE; }
    p->pipeline = gst_element_factory_make("playbin", NULL);
    if (!p->pipeline) { fail(p, "The private GStreamer playback plugin is missing."); return FALSE; }
    p->setup_handler = g_signal_connect(p->pipeline, "element-setup", G_CALLBACK(element_setup), p);
    gchar *description = g_strdup("queue max-size-buffers=1 max-size-bytes=0 max-size-time=0 ! "
        "videoconvert ! video/x-raw,format=RGBA ! "
        "appsink name=cmv_frames sync=true max-buffers=1 drop=true wait-on-eos=false");
    GError *parse_error = NULL;
    GstElement *bin = gst_parse_bin_from_description(description, TRUE, &parse_error);
    g_free(description);
    if (!bin || parse_error) {
        fail(p, parse_error ? parse_error->message : "Unable to build the RGBA video sink.");
        g_clear_error(&parse_error); if (bin) gst_object_unref(bin);
        return FALSE;
    }
    gst_object_ref_sink(bin);
    p->sink = GST_APP_SINK(gst_bin_get_by_name(GST_BIN(bin), "cmv_frames"));
    p->sink_pad = gst_element_get_static_pad(GST_ELEMENT(p->sink), "sink");
    p->probe_id = gst_pad_add_probe(p->sink_pad, GST_PAD_PROBE_TYPE_BUFFER
        | GST_PAD_PROBE_TYPE_EVENT_DOWNSTREAM | GST_PAD_PROBE_TYPE_EVENT_FLUSH, sink_probe, p, NULL);
    GstAppSinkCallbacks callbacks = {0};
    callbacks.new_preroll = new_preroll; callbacks.new_sample = new_sample;
    gst_app_sink_set_callbacks(p->sink, &callbacks, p, NULL);
    gchar *absolute = g_canonicalize_filename(p->path, NULL);
    gchar *uri = gst_filename_to_uri(absolute, &parse_error);
    g_free(absolute);
    if (!uri) { fail(p, parse_error->message); g_clear_error(&parse_error); gst_object_unref(bin); return FALSE; }
    guint flags = PLAY_VIDEO | PLAY_SOFTWARE_DECODERS;
    GstElement *audio_filter = NULL;
    if (p->audio_mode == CMV_GST_AUDIO_DIRECT) {
        flags |= PLAY_AUDIO | PLAY_SOFT_VOLUME;
        audio_filter = gst_element_factory_make_full("audiopanorama", NULL);
        if (!audio_filter) {
            fail(p, "Unable to build the audio filter.");
        }
    }
    p->audio_filter = audio_filter;
    g_object_set(p->pipeline, "uri", uri, "audio-filter", audio_filter, "video-sink", bin, "flags", flags, NULL);
    g_free(uri); gst_object_unref(bin);
    p->bus = gst_element_get_bus(p->pipeline);
    return TRUE;
}

static gboolean request_state(cmv_gst_player *p, GstState state, GstState *target)
{
    if (*target == state) return TRUE;
    if (gst_element_set_state(p->pipeline, state) == GST_STATE_CHANGE_FAILURE) {
        fail(p, "GStreamer rejected a playback state change."); return FALSE;
    }
    *target = state;
    return TRUE;
}

static void drain_bus(cmv_gst_player *p)
{
    GstMessage *message;
    while ((message = gst_bus_pop(p->bus))) {
        if (GST_MESSAGE_TYPE(message) == GST_MESSAGE_ERROR) {
            GError *error = NULL; gchar *debug = NULL;
            gst_message_parse_error(message, &error, &debug);
            gchar *text = g_strdup_printf("%s%s%s", error->message, debug ? "\n" : "", debug ? debug : "");
            fail(p, text); g_free(text); g_error_free(error); g_free(debug);
        } else if (GST_MESSAGE_TYPE(message) == GST_MESSAGE_EOS) {
            g_mutex_lock(&p->lock);
            if (p->eos_generation == p->generation) p->at_eos = TRUE;
            g_mutex_unlock(&p->lock);
        }
        gst_message_unref(message);
    }
}

static gpointer control_worker(gpointer user_data)
{
    cmv_gst_player *p = user_data;
    GstState target = GST_STATE_NULL;
    guint64 sent_generation = 0;
    gboolean prepared = FALSE;
    double applied_volume = -1, applied_panning = NAN; int applied_mute = -1;
    if (!build_pipeline(p)) goto cleanup;
    if (!request_state(p, GST_STATE_PAUSED, &target)) goto cleanup;
    for (;;) {
        drain_bus(p);
        g_mutex_lock(&p->lock);
        gboolean stop = p->stop || p->error;
        guint64 generation = p->generation;
        gint64 position = p->requested_ns;
        gboolean playing = p->desired_playing, muted = p->muted;
        double rate = p->rate, volume = p->volume, panning = p->panning;
        g_mutex_unlock(&p->lock);
        if (stop) break;
        if (!isnan(volume) && (volume != applied_volume || muted != applied_mute)) {
            g_object_set(p->pipeline, "volume", volume, "mute", muted, NULL);
            applied_volume = volume; applied_mute = muted;
        }
        if (panning != applied_panning) {
            g_object_set(p->audio_filter, "panorama", panning, NULL);
            applied_panning = panning;
        }
        GstState actual, pending;
        GstStateChangeReturn state_result = gst_element_get_state(p->pipeline, &actual, &pending, 0);
        if (state_result == GST_STATE_CHANGE_FAILURE) { drain_bus(p); fail(p, "GStreamer playback state failed."); break; }
        if (!prepared && actual == GST_STATE_PAUSED && pending == GST_STATE_VOID_PENDING) prepared = TRUE;
        if (prepared && generation != sent_generation) {
            if (!request_state(p, GST_STATE_PAUSED, &target)) break;
            if (actual == GST_STATE_PAUSED) {
                guint32 seqnum = gst_util_seqnum_next();
                g_mutex_lock(&p->lock);
                if (p->generation != generation) { g_mutex_unlock(&p->lock); continue; }
                p->expected_seqnum = seqnum; p->expected_generation = generation;
                p->segment_seqnum = 0; p->segment_generation = 0; p->eos_generation = 0;
                p->allow_publish = TRUE; p->at_eos = FALSE;
                p->snapshot.state = CMV_GST_SEEKING;
                g_mutex_unlock(&p->lock);
                GstEvent *seek = gst_event_new_seek(rate, GST_FORMAT_TIME, GST_SEEK_FLAG_FLUSH | GST_SEEK_FLAG_ACCURATE,
                    GST_SEEK_TYPE_SET, position + 1, GST_SEEK_TYPE_NONE, -1);
                gst_event_set_seqnum(seek, seqnum);
                if (!gst_element_send_event(p->pipeline, seek)) { drain_bus(p); fail(p, "GStreamer rejected an accurate seek."); break; }
                sent_generation = generation;
                continue;
            }
        }
        g_mutex_lock(&p->lock);
        if (generation == p->generation && generation == sent_generation) {
            if (p->completed_generation != generation && actual == GST_STATE_PAUSED && pending == GST_STATE_VOID_PENDING) {
                if (p->preroll_generation == generation) {
                    p->completed_generation = generation; p->allow_publish = FALSE;
                    p->position_ns = p->requested_ns; p->snapshot.time_ms = p->position_ns / GST_MSECOND;
                } else if (p->eos_generation == generation) {
                    p->completed_generation = generation; p->allow_publish = FALSE; p->at_eos = TRUE;
                }
            }
            if (p->completed_generation == generation) {
                if (p->at_eos) {
                    p->allow_publish = FALSE; p->snapshot.state = CMV_GST_ENDED;
                } else if (!playing && actual == GST_STATE_PAUSED && pending == GST_STATE_VOID_PENDING) {
                    p->allow_publish = FALSE; p->snapshot.state = CMV_GST_PAUSED;
                } else if (playing && actual == GST_STATE_PLAYING && pending == GST_STATE_VOID_PENDING) {
                    p->snapshot.state = CMV_GST_PLAYING;
                }
            }
        }
        gboolean complete = p->completed_generation == generation && p->generation == generation;
        gboolean ended = p->at_eos;
        if (complete && playing && !ended) p->allow_publish = TRUE;
        g_mutex_unlock(&p->lock);
        if (complete && !ended && !request_state(p, playing ? GST_STATE_PLAYING : GST_STATE_PAUSED, &target)) break;
        gint64 queried;
        if (prepared && gst_element_query_position(p->pipeline, GST_FORMAT_TIME, &queried)) {
            g_mutex_lock(&p->lock);
            if (generation == p->generation) { p->position_ns = queried; p->snapshot.time_ms = queried / GST_MSECOND; }
            g_mutex_unlock(&p->lock);
        }
        if (prepared && gst_element_query_duration(p->pipeline, GST_FORMAT_TIME, &queried)) {
            g_mutex_lock(&p->lock); p->snapshot.duration_ms = queried / GST_MSECOND; g_mutex_unlock(&p->lock);
        }
        g_mutex_lock(&p->lock);
        if (!p->stop && !p->error) g_cond_wait_until(&p->wake, &p->lock, g_get_monotonic_time() + CONTROL_POLL_US);
        g_mutex_unlock(&p->lock);
    }
cleanup:
    if (p->pipeline) {
        gst_element_set_state(p->pipeline, GST_STATE_NULL);
        gst_element_get_state(p->pipeline, NULL, NULL, GST_CLOCK_TIME_NONE);
    }
    if (p->sink) {
        GstAppSinkCallbacks callbacks = {0}; gst_app_sink_set_callbacks(p->sink, &callbacks, NULL, NULL);
    }
    if (p->sink_pad) {
        if (p->probe_id) gst_pad_remove_probe(p->sink_pad, p->probe_id);
        gst_object_unref(p->sink_pad);
    }
    if (p->sink) gst_object_unref(p->sink);
    if (p->setup_handler) g_signal_handler_disconnect(p->pipeline, p->setup_handler);
    if (p->bus) gst_object_unref(p->bus);
    if (p->pipeline) gst_object_unref(p->pipeline);
    return NULL;
}

int32_t cmv_gst_create(const char *path,
    int32_t audio_mode, cmv_gst_player **out, char *error, uint64_t capacity)
{
    if (out) *out = NULL;
    if (!path || !*path || !out
        || (audio_mode != CMV_GST_AUDIO_NONE && audio_mode != CMV_GST_AUDIO_DIRECT)) {
        copy_error(error, capacity, "Invalid video path or audio mode."); return 0;
    }
    cmv_gst_player *p = g_try_new0(cmv_gst_player, 1);
    if (!p) { copy_error(error, capacity, "Unable to allocate a video player."); return 0; }
    g_mutex_init(&p->lock); g_cond_init(&p->wake);
    p->path = g_strdup(path); p->audio_mode = audio_mode;
    p->width = p->height = 0;
    p->rate = 1; p->volume = NAN; p->panning = 0; p->generation = 1;
    p->snapshot.state = CMV_GST_OPENING;
    GError *thread_error = NULL;
    p->worker = g_thread_try_new("cmv-gstreamer", control_worker, p, &thread_error);
    if (!p->worker) {
        copy_error(error, capacity, thread_error->message); g_error_free(thread_error);
        g_free(p->path); g_free(p->pixels); g_cond_clear(&p->wake); g_mutex_clear(&p->lock); g_free(p); return 0;
    }
    *out = p; copy_error(error, capacity, NULL); return 1;
}

static gboolean accept_locked(cmv_gst_player *p) { return !p->stop && !p->error; }

int32_t cmv_gst_play(cmv_gst_player *p)
{
    if (!p) return 0;
    g_mutex_lock(&p->lock); gboolean accepted = accept_locked(p);
    if (accepted) {
        if (p->at_eos) {
            p->requested_ns = 0; p->generation++; p->at_eos = FALSE; p->snapshot.state = CMV_GST_SEEKING;
        }
        p->desired_playing = TRUE; g_cond_signal(&p->wake);
    }
    g_mutex_unlock(&p->lock); return accepted;
}

int32_t cmv_gst_pause(cmv_gst_player *p)
{
    if (!p) return 0;
    g_mutex_lock(&p->lock); gboolean accepted = accept_locked(p);
    if (accepted) { p->desired_playing = FALSE; g_cond_signal(&p->wake); }
    g_mutex_unlock(&p->lock); return accepted;
}

uint64_t cmv_gst_seek_ns(cmv_gst_player *p, int64_t time_ns)
{
    if (!p) return 0;
    if (time_ns < 0 || time_ns == G_MAXINT64) { fail(p, "Seek time is outside the supported nanosecond range."); return 0; }
    g_mutex_lock(&p->lock); guint64 generation = 0;
    if (accept_locked(p)) {
        p->requested_ns = time_ns; generation = ++p->generation;
        p->at_eos = FALSE; p->snapshot.state = CMV_GST_SEEKING; g_cond_signal(&p->wake);
    }
    g_mutex_unlock(&p->lock); return generation;
}

int32_t cmv_gst_set_rate(cmv_gst_player *p, double rate)
{
    if (!p) return 0;
    if (!isfinite(rate) || rate <= 0) { fail(p, "Playback rate must be finite and positive."); return 0; }
    g_mutex_lock(&p->lock); gboolean accepted = accept_locked(p);
    if (accepted && rate != p->rate) {
        if (p->generation == p->completed_generation) p->requested_ns = p->position_ns;
        p->rate = rate; p->generation++; p->at_eos = FALSE;
        p->snapshot.state = CMV_GST_SEEKING; g_cond_signal(&p->wake);
    }
    g_mutex_unlock(&p->lock); return accepted;
}

int32_t cmv_gst_set_volume(cmv_gst_player *p, double volume)
{
    if (!p) return 0;
    if (!isfinite(volume) || volume < 0 || volume > 1) { fail(p, "Video volume must be between zero and one."); return 0; }
    g_mutex_lock(&p->lock); gboolean accepted = accept_locked(p);
    if (accepted) { p->volume = volume; g_cond_signal(&p->wake); }
    g_mutex_unlock(&p->lock); return accepted;
}

int32_t cmv_gst_set_panning(cmv_gst_player *p, double panning)
{
    if (!p) return 0;
    if (!isfinite(panning) || panning < -1 || panning > 1) { fail(p, "Audio panning must be between negative one and one."); return 0; }
    g_mutex_lock(&p->lock); gboolean accepted = accept_locked(p);
    if (accepted) { p->panning = panning; g_cond_signal(&p->wake); }
    g_mutex_unlock(&p->lock); return accepted;
}

int32_t cmv_gst_set_muted(cmv_gst_player *p, int32_t muted)
{
    if (!p) return 0;
    g_mutex_lock(&p->lock); gboolean accepted = accept_locked(p);
    if (accepted) { p->muted = muted != 0; g_cond_signal(&p->wake); }
    g_mutex_unlock(&p->lock); return accepted;
}

int32_t cmv_gst_get_snapshot(cmv_gst_player *p, cmv_gst_snapshot *snapshot)
{
    if (!p || !snapshot) return 0;
    g_mutex_lock(&p->lock);
    *snapshot = p->snapshot;
    snapshot->width = p->width;
    snapshot->height = p->height;
    snapshot->desired_playing = p->desired_playing;
    snapshot->has_frame = p->frame.serial != 0 && p->frame.generation == p->generation;
    snapshot->generation = p->generation; snapshot->completed_generation = p->completed_generation;
    snapshot->frame_serial = p->frame.serial; snapshot->rate = p->rate;
    g_mutex_unlock(&p->lock); return 1;
}

uint64_t cmv_gst_get_error(cmv_gst_player *p, char *error, uint64_t capacity)
{
    if (!p) { copy_error(error, capacity, "Invalid video player."); return sizeof("Invalid video player."); }
    g_mutex_lock(&p->lock); uint64_t required = p->error ? strlen(p->error) + 1 : 1;
    copy_error(error, capacity, p->error); g_mutex_unlock(&p->lock); return required;
}

int32_t cmv_gst_copy_rgba(cmv_gst_player *p, void *destination, int32_t stride,
    uint64_t capacity, uint64_t after_serial, cmv_gst_frame *frame)
{
    if (!p || !destination || !frame || stride < 0 || (gsize)stride < p->row_bytes
        || capacity < (guint64)stride * (p->height - 1) + p->row_bytes) return -1;
    g_mutex_lock(&p->lock);
    if (p->frame.serial <= after_serial || p->frame.generation != p->generation || p->error) {
        g_mutex_unlock(&p->lock); return 0;
    }
    for (int y = 0; y < p->height; ++y)
        memcpy((guint8 *)destination + (gsize)y * stride, p->pixels + (gsize)y * p->row_bytes, p->row_bytes);
    *frame = p->frame;
    g_mutex_unlock(&p->lock); return 1;
}

void cmv_gst_destroy(cmv_gst_player *p)
{
    if (!p) return;
    g_mutex_lock(&p->lock); p->stop = TRUE; p->allow_publish = FALSE; g_cond_signal(&p->wake); g_mutex_unlock(&p->lock);
    g_thread_join(p->worker);
    g_free(p->pixels); g_free(p->path); g_free(p->error);
    g_cond_clear(&p->wake); g_mutex_clear(&p->lock); g_free(p);
}
