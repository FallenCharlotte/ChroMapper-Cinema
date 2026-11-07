#ifndef CMV_GSTREAMER_H
#define CMV_GSTREAMER_H

#include <stdint.h>

#if defined(_WIN32)
#define CMV_GST_API __declspec(dllexport)
#else
#define CMV_GST_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct cmv_gst_player cmv_gst_player;

typedef enum cmv_gst_state {
    CMV_GST_OPENING = 0,
    CMV_GST_PAUSED = 1,
    CMV_GST_PLAYING = 2,
    CMV_GST_SEEKING = 3,
    CMV_GST_ENDED = 4,
    CMV_GST_ERROR = 5
} cmv_gst_state;

typedef enum cmv_gst_audio_mode {
    CMV_GST_AUDIO_NONE = 0,
    CMV_GST_AUDIO_DIRECT = 1
} cmv_gst_audio_mode;

typedef struct cmv_gst_snapshot {
    int32_t state;
    uint32_t width;
    uint32_t height;
    int32_t desired_playing;
    int32_t has_frame;
    int32_t error_code;
    uint64_t generation;
    uint64_t completed_generation;
    uint64_t frame_serial;
    int64_t time_ms;
    int64_t duration_ms;
    double rate;
} cmv_gst_snapshot;

typedef struct cmv_gst_frame {
    uint64_t serial;
    uint64_t generation;
    int64_t raw_pts_ns;
    int64_t stream_time_ns;
    int64_t duration_ns;
    int32_t width;
    int32_t height;
} cmv_gst_frame;

/* Configure once before create. The caller creates the registry parent directory.
 * Initialization runs on the first player's worker. GStreamer configuration is
 * process-wide; plugin_dir and registry_path must be absolute private paths.
 * A NULL/empty scanner_path selects in-process plugin scanning. Repeating the
 * same configuration is accepted, including after a managed domain reload.
 * Gst environment changes are restored after initialization. */
CMV_GST_API int32_t cmv_gst_configure(const char *plugin_dir,
    const char *registry_path, const char *scanner_path,
    char *error, uint64_t error_capacity);

/* Create returns before preparation/decoding. Observe asynchronous errors with
 * get_snapshot/get_error. The file path and authored coded dimensions are copied. */
CMV_GST_API int32_t cmv_gst_create(const char *file_path,
    int32_t audio_mode, cmv_gst_player **out_player,
    char *error, uint64_t error_capacity);
CMV_GST_API int32_t cmv_gst_play(cmv_gst_player *player);
CMV_GST_API int32_t cmv_gst_pause(cmv_gst_player *player);
/* Returns the new generation, or zero for an invalid/rejected request. A seek
 * uses FLUSH|ACCURATE at requested time + 1 ns to exclude a measured 1 ns
 * preceding-frame overlap at exact composition-time boundaries. A request 1 ns
 * before a rounded boundary can therefore select the following frame. */
CMV_GST_API uint64_t cmv_gst_seek_ns(cmv_gst_player *player, int64_t time_ns);
CMV_GST_API int32_t cmv_gst_set_rate(cmv_gst_player *player, double rate);
/* Volume is linear [0,1]. None audio mode never enables an audio stream. */
CMV_GST_API int32_t cmv_gst_set_volume(cmv_gst_player *player, double volume);
CMV_GST_API int32_t cmv_gst_set_panning(cmv_gst_player *player, double panning);
CMV_GST_API int32_t cmv_gst_set_muted(cmv_gst_player *player, int32_t muted);
CMV_GST_API int32_t cmv_gst_get_snapshot(cmv_gst_player *player,
    cmv_gst_snapshot *snapshot);
/* Returns the required UTF-8 buffer size including NUL (1 when no error). */
CMV_GST_API uint64_t cmv_gst_get_error(cmv_gst_player *player,
    char *error, uint64_t error_capacity);
/* Copies a newer current-generation frame; 1=copied, 0=no new frame,
 * -1=invalid destination. Output is tightly sampled RGBA with bottom-up rows
 * for Texture2D; destination_stride may include caller padding. The copy is
 * synchronized with publication. No callbacks or Unity API calls cross this ABI. */
CMV_GST_API int32_t cmv_gst_copy_rgba(cmv_gst_player *player, void *destination,
    int32_t destination_stride, uint64_t capacity, uint64_t after_serial,
    cmv_gst_frame *frame);
/* Exclude concurrent calls while destroying. Stops the pipeline, joins the
 * control worker, disconnects callbacks and releases all per-player resources. */
CMV_GST_API void cmv_gst_destroy(cmv_gst_player *player);

#ifdef __cplusplus
}
#endif
#endif
