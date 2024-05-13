**FFDaemon - C# Video Encoding Daemon**

**Introduction**

FFDaemon is a lightweight C# daemon designed to automate video encoding in a designated directory using the powerful ffmpeg library. It continuously monitors the directory for new video files and encodes them according to your chosen settings.

**Features**

* **Background processing:** Operates as a daemon, freeing your terminal and running silently in the background.
* **Configurable:** Supports various command-line options for customization, including:
    * Input and output directories
    * Supported input video formats
    * Output video format and codec
    * Additional ffmpeg input and output arguments
    * Aspect ratio, audio encoding, video stream handling
    * File deletion options (old files, empty directories, temporary files)
    * FFmpeg process termination on exit
    * Maximum history size
    * Maximum ffmpeg output buffer size
* **Interactive mode (optional):** Provides real-time feedback on encoding progress (default behavior).
* **Automatic file monitoring:** Continuously scans the specified directory for new files to encode.

**Requirements**

* **C# .NET Framework or .NET Core:** Ensure you have a compatible .NET runtime environment installed on your system.
* **ffmpeg:** Download and install the latest version of ffmpeg from the official website ([https://ffmpeg.org/download.html](https://ffmpeg.org/download.html)). Make sure it's accessible in your system's PATH for FFDaemon to locate it.

**Usage**

1. Open a terminal window and navigate to the output directory where the FFDaemon executable resides.
2. Run the following command, providing desired options (replace `<directory>` with the path to your video directory):

   ```bash
   ffdaemon -d <directory> [other options]
   ```

**Available Options**

Absolutely, here's the updated table incorporating the new information from the provided C# code snippet:

| Option | Short Flag | Long Flag | Description | Default |
|---|---|---|---|---|
| Configuration File | `-c` | `--config` | Set configuration file path (optional) | N/A |
| Interactive Mode | `-i` | `--interactive` | Enable real-time encoding progress feedback (default) | `true` |
| Working Directory | `-d` | `--directory` | Force the working directory where FFDaemon monitors for videos | Current directory |
| Output Directory | `-o` | `--output` | Force the directory where encoded videos are saved | `../Encoded` |
| Allowed Inputs | N/A | N/A | List of supported video input formats (read-only) | `.mkv`, `.avi`, `.vp9`, `.ts`, `.mp4`, `.webm` |
| Output Extension | `-x` | `--output-extension` | Set the output video extension | `mkv` |
| Video Codec | `-v` | `--video-codec` | Set the desired video codec for encoding | `vp9` |
| ffmpeg Input Args | `--input-args` | `--ffmpeg-input-args` | Additional base arguments for ffmpeg input (optional) | `-y -probesize 1000000000 -analyzeduration 100000000` |
| ffmpeg Output Args | `--output-args` | `--ffmpeg-output-args` | Additional base arguments for ffmpeg output (optional) | `""` (empty) |
| Force 1:1 Aspect Ratio | `--force-11-ar` | `--force-aspect-ratio-1-1` | Force a 1:1 aspect ratio for encoded videos (optional) | `true` |
| Smart Audio Encoding | `--smart-audio` | `--enable-smart-audio` | Enable smart audio encoding based on input characteristics (optional) | `true` |
| Keep One Video Stream | `--keep-one-video` | `--keep-single-video-stream` | Keep only one video stream during encoding (optional) | `true` |
| Remove Old Files | `--remove-old-file` | `--delete-old-encoded-files` | Remove old encoded files corresponding to newly encoded ones (optional) | `true` |
| Kill ffmpeg | `--kill-ffmpeg` | `--kill-ffmpeg-when-exited` | Kill ffmpeg process when FFDaemon exits | `true` |
| Delete Empty Directories | `--delete-empty-directories` | `--delete-empty-directories` | Delete empty directories after processing | `true` |
| Delete Temporary File | `--delete-temporary-file` | `--delete-temporary-file` | Delete temporary files created during processing | `true` |
| Max History Size | `-h` | `--max-history-size` | Set max history size (number of processed files to remember) | 100 |
| Max ffmpeg Buffer Size | `-b` | `--max-ffmpeg-buffer` | Set max ffmpeg output buffer size | 25000 |
| Wait Time Between Scans | `-w` | `--wait-time` | Set time to wait between scans for new videos | 60 seconds |
| Start Activity Time | `--start-at` | `--start-at` | Set a specific time to start encoding activity (optional) | null |
| Stop Activity Time | `--stop-at` | `--stop-at` | Set a specific time to stop encoding activity (optional) | null |
| After Start Command | `--after-start` | `--after-start` |  Execute a custom command after FFDaemon starts (optional) | null |
| After Stop Command | `--after-stop` | `--after-stop` | Execute a custom command after FFDaemon stops (optional) | null |
