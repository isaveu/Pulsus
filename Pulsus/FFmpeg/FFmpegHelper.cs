﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using SDL2;

namespace Pulsus.FFmpeg
{
	public static class FFmpegHelper
	{
		private static string _ffmpegPath;
		public static string ffmpegPath
		{
			get
			{
				if (_ffmpegPath == null)
					_ffmpegPath = Path.Combine(Program.basePath, "ffmpeg", Environment.Is64BitProcess ? "x64" : "x86");

				return _ffmpegPath;
			}
		}

		public static string logLastLine { get; private set; }

		private static LogDelegate logCallback = Log;
		private static IntPtr logPtr = IntPtr.Zero;

		// prevents garbage collector from collecting delegates
		private static List<Delegate> delegateRefs = new List<Delegate>();

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void LogDelegate(IntPtr ptr, int i, string str, IntPtr valist);

		static void Log(IntPtr avcl, int level, string fmt, IntPtr vl)
		{
			logLastLine = fmt.Trim();

			if (level > AV_LOG_WARNING)
				return;

			System.Diagnostics.Debug.WriteLine("ffmpeg: " + logLastLine);
		}

		public static void Init()
		{
			// specify additional search path where ffmpeg binaries should be loaded from
			string envVariable = Environment.OSVersion.Platform == PlatformID.Win32NT ? "PATH" : "LD_LIBRARY_PATH";
			string oldValue = Environment.GetEnvironmentVariable(envVariable);
			Environment.SetEnvironmentVariable(envVariable, ffmpegPath + Path.PathSeparator + oldValue);

			try
			{
				ffmpeg.av_register_all();
				ffmpeg.avcodec_register_all();

				// early load
				ffmpeg.swscale_version();
				ffmpeg.swresample_version();

				// forward FFmpeg log messages
				unsafe
				{
					logPtr = Marshal.GetFunctionPointerForDelegate(logCallback);
					GC.KeepAlive(logCallback);
					ffmpeg.av_log_set_callback(logPtr);
				}
			}
			catch (DllNotFoundException e)
			{
				string description = "Failed to initialize FFmpeg.";
				if (Environment.OSVersion.Platform != PlatformID.Win32NT)
					description += " Please install FFmpeg (3.0.2), or install the static FFmpeg binaries to \"" + ffmpegPath + "\"";

				throw new DllNotFoundException(description, e);
			}
		}

		public static byte[] ImageFromFile(string path, out int width, out int height, out int bytesPerPixel)
		{
			using (FFmpegVideo video = new FFmpegVideo())
			{
				video.Load(path);

				width = video.width;
				height = video.height;
				bytesPerPixel = 4;

				return video.ReadFrame();
			}
		}

		public static byte[] SoundFromFile(string path, out int sampleRate, out int channels, out ushort sampleFormatSDL)
		{
			using (FFmpegContext ffContext = FFmpegContext.Read(path))
			{
				ffContext.SelectStream(AVMediaType.AVMEDIA_TYPE_AUDIO);

				sampleRate = ffContext.audioSampleRate;
				channels = ffContext.audioChannels;

				AVSampleFormat sampleFormat = ffContext.audioSampleFormat;
				switch (sampleFormat)
				{
					case AVSampleFormat.AV_SAMPLE_FMT_S16:
						sampleFormatSDL = SDL.AUDIO_S16;
						break;
					case AVSampleFormat.AV_SAMPLE_FMT_FLT:
						sampleFormatSDL = SDL.AUDIO_F32;
						break;
					case AVSampleFormat.AV_SAMPLE_FMT_S32:
						sampleFormatSDL = SDL.AUDIO_S32;
						break;
					default:
						throw new ApplicationException("Could not map AVSampleFormat to SDL audio format: " + sampleFormat.ToString());
				}

				// FFmpeg only approximates stream durations but is
				// usually not far from the real duration.
				byte[] bytes = new byte[ffContext.audioBytesTotal];

				// read all data from frames
				long offset = 0;
				while (ffContext.ReadFrame())
				{
					long frameSize = ffContext.GetFrameBufferSize();
					if (offset + frameSize > bytes.Length)
						Array.Resize(ref bytes, (int)(offset + frameSize));

					offset += ffContext.GetFrameData(ref bytes, (int)offset);
				}

				return bytes;
			}
		}

		public static byte[] SoundFromFileResample(string path, int sampleRate, int channels, ushort sampleFormatSDL, ResampleQuality resampleQuality = ResampleQuality.High)
		{
			AVSampleFormat targetFormat2;
			switch (sampleFormatSDL)
			{
				case SDL.AUDIO_S16:
					targetFormat2 = AVSampleFormat.AV_SAMPLE_FMT_S16;
					break;
				case SDL.AUDIO_F32:
					targetFormat2 = AVSampleFormat.AV_SAMPLE_FMT_FLT;
					break;
				case SDL.AUDIO_S32:
					targetFormat2 = AVSampleFormat.AV_SAMPLE_FMT_S32;
					break;
				default:
					throw new ApplicationException("Could not map SDL audio format to AVSampleFormat: " + sampleFormatSDL.ToString());
			}

			using (FFmpegContext ffContext = FFmpegContext.Read(path))
			{
				ffContext.SelectStream(AVMediaType.AVMEDIA_TYPE_AUDIO);

				// setup resamplers and other format converters if needed
				ffContext.ConvertToFormat(targetFormat2, sampleRate, channels, resampleQuality);

				// FFmpeg only approximates stream durations but is
				// usually not far from the real duration.
				byte[] bytes = new byte[ffContext.audioBytesTotal];

				// read all data from frames
				long offset = 0;
				while (ffContext.ReadFrame())
				{
					long frameSize = ffContext.GetFrameBufferSize();
					if (offset + frameSize > bytes.Length)
						Array.Resize(ref bytes, (int)(offset + frameSize));

					offset += ffContext.GetFrameData(ref bytes, (int)offset);
				}

				return bytes;
			}
		}

		// compression level [0-9]
		public static void SaveImagePNG(string path, byte[] data, int width, int height, int compression = 1)
		{
			using (FFmpegContext ffContext = FFmpegContext.Write(path))
			{
				ffContext.SetOutputFormat(AVCodecID.AV_CODEC_ID_PNG, width, height, AVPixelFormat.AV_PIX_FMT_BGRA, compression);
				ffContext.WriteHeader();
				ffContext.WriteFrame(data);
			}
		}

		public static void SaveSound(string path, byte[] data, int sampleCount, int targetSampleRate)
		{
			using (FFmpegContext ffContext = FFmpegContext.Write(path))
			{
				ffContext.SetOutputFormat(AVCodecID.AV_CODEC_ID_NONE, targetSampleRate, sampleCount, AVSampleFormat.AV_SAMPLE_FMT_S16);
				ffContext.WriteHeader();
				ffContext.WriteFrame(data);
			}
		}

		public const int AV_LOG_QUIET = -8;
		public const int AV_LOG_PANIC = 0;
		public const int AV_LOG_FATAL = 8;
		public const int AV_LOG_ERROR = 16;
		public const int AV_LOG_WARNING = 24;
		public const int AV_LOG_INFO = 32;
		public const int AV_LOG_VERBOSE = 40;
		public const int AV_LOG_DEBUG = 48;
		public const int AV_LOG_TRACE = 56;
	}
}
