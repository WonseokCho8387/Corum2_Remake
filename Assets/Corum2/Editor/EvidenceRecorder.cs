using System.IO;
using Corum2.Unity;
using UnityEditor.Media;
using UnityEngine;

namespace Corum2.EditorTools
{
    /// <summary>
    /// Video evidence (V) for Play mode runs, driven from the CLI with script eval.
    /// GameHost renders the main camera (with the HUD) into a fixed-size texture every frame; this class encodes those
    /// frames to H.264 MP4 with Unity's MediaEncoder. Time.captureFramerate locks each frame to 1/fps game seconds, so
    /// at 50 fps every video frame is exactly one 50 Hz fixed tick. Works when the Editor Game view is hidden.
    /// </summary>
    public static class EvidenceRecorder
    {
        static MediaEncoder _encoder;
        static int _frames;
        static string _path;

        public static string Start(string outputPathWithoutExtension, int width, int height, int fps)
        {
            var host = GameHost.Instance;
            if (host == null || !Application.isPlaying) return "not playing";
            if (_encoder != null) return "already recording";
            _path = outputPathWithoutExtension + ".mp4";
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var attrs = new VideoTrackAttributes
            {
                frameRate = new MediaRational(fps),
                width = (uint)width,
                height = (uint)height,
                includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.High,
            };
            _encoder = new MediaEncoder(_path, attrs);
            _frames = 0;
            Time.captureFramerate = fps;
            host.CaptureWidth = width;
            host.CaptureHeight = height;
            host.AnnotateManifest("capture", new Corum2.Core.YamlMap().Add("path", _path.Replace('\\', '/')).Add("encoder", "MediaEncoder H.264")
                .Add("width", width).Add("height", height).Add("fps", fps).Add("time_mode", "Time.captureFramerate=" + fps + " (game time, one frame per tick)")
                .Add("note", "not device latency or FPS evidence"));
            var stillDir = outputPathWithoutExtension + "_stills";
            Directory.CreateDirectory(stillDir);
            host.FrameSink = tex =>
            {
                _encoder.AddFrame(tex);
                // One still per second of game time (frame index = tick offset from recording start).
                if (_frames % fps == 0)
                    File.WriteAllBytes(Path.Combine(stillDir, "f" + _frames.ToString("D5") + ".png"), tex.EncodeToPNG());
                _frames++;
            };
            host.Sim.Log.Emit("marker", "", "", "", "", "VideoRecordingStarted", ("file", Path.GetFileName(_path)), ("size", width + "x" + height), ("fps", fps),
                ("frameLock", "Time.captureFramerate=" + fps));
            return "recording " + _path;
        }

        public static string Stop()
        {
            var host = GameHost.Instance;
            if (host != null)
            {
                host.FrameSink = null;
                host.Sim?.Log.Emit("marker", "", "", "", "", "VideoRecordingStopped", ("frames", _frames));
            }
            Time.captureFramerate = 0;
            if (_encoder == null) return "not recording";
            _encoder.Dispose();
            _encoder = null;
            return "stopped " + _frames + " frames -> " + _path;
        }
    }
}
