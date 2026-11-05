#!/bin/bash

cc cmv_gstreamer.c "$@" -shared -o ChroMapper_Data/Plugins/libcmv_gstreamer.so `pkg-config --cflags --libs gstreamer-1.0 gstreamer-app-1.0 gstreamer-video-1.0` -l:libgobject-2.0.so.0 -l:libglib-2.0.so.0
