using System.IO;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace Corum2.EditorTools
{
    /// <summary>
    /// Video evidence (V) for Play mode runs, driven from the CLI with script eval.
    /// Records the Game view at the given size (the Game view is resized to it, so screen and camera aspect match).
    /// Constant frame rate: with fps = 50 every recorded frame is exactly one 50 Hz fixed tick of game time.
    /// </summary>
    public static class EvidenceRecorder
    {
        static RecorderController _controller;
        static MovieRecorderSettings _settings;

        public static string Start(string outputPathWithoutExtension, int width, int height, float fps)
        {
            if (_controller != null && _controller.IsRecording()) return "already recording";
            Directory.CreateDirectory(Path.GetDirectoryName(outputPathWithoutExtension));
            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            _controller = new RecorderController(controllerSettings);

            _settings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            _settings.name = "Corum2 evidence";
            _settings.Enabled = true;
            _settings.EncoderSettings = new CoreEncoderSettings
            {
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Medium,
                Codec = CoreEncoderSettings.OutputCodec.MP4,
            };
            _settings.CaptureAlpha = false;
            _settings.AudioInputSettings.PreserveAudio = false;
            _settings.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
            _settings.OutputFile = outputPathWithoutExtension;

            controllerSettings.AddRecorderSettings(_settings);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = fps;
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            controllerSettings.CapFrameRate = true;

            RecorderOptions.VerboseMode = false;
            _controller.PrepareRecording();
            var ok = _controller.StartRecording();
            return ok ? "recording " + outputPathWithoutExtension + ".mp4 " + width + "x" + height + " @" + fps : "failed to start";
        }

        public static string Stop()
        {
            if (_controller == null) return "not recording";
            _controller.StopRecording();
            _controller = null;
            return "stopped";
        }
    }
}
