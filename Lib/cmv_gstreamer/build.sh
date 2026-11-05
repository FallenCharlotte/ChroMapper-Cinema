#!/bin/bash

cc cmv_gstreamer.c -DSYSTEM_GST -shared -o libcmv_gstreamer.so `pkg-config --cflags --libs gstreamer-1.0 gstreamer-app-1.0 gstreamer-video-1.0` -l:libgobject-2.0.so.0 -l:libglib-2.0.so.0
