﻿using System;
using Pulsus.Gameplay;
using System.IO;
using Pulsus.FFmpeg;

namespace Pulsus
{
	public class RenderAudioScene : Scene
	{
		BGM songPlayer;
		Player autoplay;

		public RenderAudioScene(Game game, Song song, string renderPath) : base(game)
		{
			active = false;
			if (song == null)
				return;

			song.Load();

			if (string.IsNullOrWhiteSpace(Path.GetExtension(renderPath)))
				renderPath += ".wav";

			song.GenerateEvents();

			foreach (var sound in song.chart.soundObjects)
				sound.Value.Load(song.path);

			songPlayer = new BGM(audio, song);
			autoplay = new Player(audio, song, null, null);

			songPlayer.realtime = false;
			autoplay.realtime = false;

			autoplay.autoplay = true;

			songPlayer.StartPlayer();
			autoplay.StartPlayer();

			songPlayer.Update(1.0f);
			autoplay.Update(1.0f);

			if (songPlayer.playing || autoplay.playing)
				throw new ApplicationException("Players did not finish as expected");

			byte[] audioData = audio.RenderAudio();
			if (audioData.Length > 0)
			{
				FFmpegHelper.SaveSound(renderPath,
					audioData, audioData.Length/4, audio.audioSpec.freq);
			}
		}

		public override void Dispose()
		{
			autoplay.Dispose();
			songPlayer.Dispose();
		}

		public override void Draw(double deltaTime)
		{
		}

		public override void Update(double deltaTime)
		{
		}
	}
}
