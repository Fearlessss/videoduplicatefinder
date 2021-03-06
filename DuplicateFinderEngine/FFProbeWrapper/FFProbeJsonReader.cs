using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DuplicateFinderEngine.FFProbeWrapper {
	static class FFProbeJsonReader {

		enum JsonObjects {
			None,
			Streams,
			Format
		}

		static readonly byte[] StreamsKeyword = { 0x73, 0x74, 0x72, 0x65, 0x61, 0x6D, 0x73 }; // = streams
		static readonly byte[] FormatKeyword = { 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74 }; // = format
		private static readonly byte[] IndexKeyword = { 0x69, 0x6E, 0x64, 0x65, 0x78 }; // = index

		/// <summary>
		/// Parses FFprobe JSON output and returns a new <see cref="MediaInfo"/>
		/// </summary>
		/// <param name="data">JSON output</param>
		/// <param name="file">The file the JSON output format is about</param>
		/// <returns><see cref="MediaInfo"/> containing information from FFprobe output</returns>
		public static MediaInfo Read(byte[] data, string file) {
			var json = new Utf8JsonReader(data, isFinalBlock: true, state: default);

			var streams = new List<Dictionary<string, object>>();
			var format = new Dictionary<string, object>();

			var currentStream = -1;

			var currentObject = JsonObjects.None;
			string lastKey = null;

			while (json.Read()) {
				JsonTokenType tokenType = json.TokenType;
				ReadOnlySpan<byte> valueSpan = json.ValueSpan;
				switch (tokenType) {
				case JsonTokenType.StartObject:
				case JsonTokenType.EndObject:
				case JsonTokenType.Null:
				case JsonTokenType.StartArray:
				case JsonTokenType.EndArray:
					break;
				case JsonTokenType.PropertyName:
					if (valueSpan.SequenceEqual(StreamsKeyword.AsSpan())) {
						currentObject = JsonObjects.Streams;
						break;
					}
					if (valueSpan.SequenceEqual(FormatKeyword.AsSpan())) {
						currentObject = JsonObjects.Format;
						break;
					}

					if (valueSpan.SequenceEqual(IndexKeyword.AsSpan())) {
						streams.Add(new Dictionary<string, object>());
						currentStream++;
					}

					if (currentObject == JsonObjects.Streams) {
						lastKey = json.GetStringValue();
						streams[currentStream].Add(lastKey, null);
					}
					else if (currentObject == JsonObjects.Format) {
						lastKey = json.GetStringValue();
						format.Add(lastKey, null);
					}
					break;
				case JsonTokenType.String:
					if (currentObject == JsonObjects.Streams) {
						streams[currentStream][lastKey] = json.GetStringValue();
					}
					else if (currentObject == JsonObjects.Format) {
						format[lastKey] = json.GetStringValue();
					}
					break;
				case JsonTokenType.Number:
					if (!json.TryGetInt32Value(out int valueInteger)) {
#if DEBUG
						System.Diagnostics.Trace.TraceWarning($"JSON number parse error: \"{lastKey}\" = {System.Text.Encoding.UTF8.GetString(valueSpan.ToArray())}, file = {file}");
#endif
						break;
					}

					if (currentObject == JsonObjects.Streams) {
						streams[currentStream][lastKey] = valueInteger;
					}
					else if (currentObject == JsonObjects.Format) {
						format[lastKey] = valueInteger;
					}
					break;
				case JsonTokenType.True:
				case JsonTokenType.False:
					bool valueBool = json.GetBooleanValue();
					if (currentObject == JsonObjects.Streams) {
						streams[currentStream][lastKey] = valueBool;
					}
					else if (currentObject == JsonObjects.Format) {
						format[lastKey] = valueBool;
					}
					break;
				default:
					throw new ArgumentException();
				}
			}

			var info = new MediaInfo {
				Streams = new MediaInfo.StreamInfo[streams.Count]
			};

			if (format.ContainsKey("duration") && TimeSpan.TryParse((string)format["duration"], out var duration))
				info.Duration = duration;

			for (int i = 0; i < streams.Count; i++) {
				info.Streams[i] = new MediaInfo.StreamInfo();
				if (streams[i].ContainsKey("bit_rate") && long.TryParse((string)streams[i]["bit_rate"], out var bitrate))
					info.Streams[i].BitRate = bitrate;
				if (streams[i].ContainsKey("width"))
					info.Streams[i].Width = (int)streams[i]["width"];
				if (streams[i].ContainsKey("height"))
					info.Streams[i].Width = (int)streams[i]["height"];
				if (streams[i].ContainsKey("codec_name"))
					info.Streams[i].CodecName = (string)streams[i]["codec_name"];
				if (streams[i].ContainsKey("codec_long_name"))
					info.Streams[i].CodecLongName = (string)streams[i]["codec_long_name"];
				if (streams[i].ContainsKey("codec_type"))
					info.Streams[i].CodecType = (string)streams[i]["codec_type"];
				if (streams[i].ContainsKey("channel_layout"))
					info.Streams[i].ChannelLayout = (string)streams[i]["channel_layout"];

				if (streams[i].ContainsKey("pix_fmt"))
					info.Streams[i].PixelFormat = (string)streams[i]["pix_fmt"];
				if (streams[i].ContainsKey("sample_rate") && int.TryParse((string)streams[i]["sample_rate"], out var sample_rate))
					info.Streams[i].SampleRate = sample_rate;
				if (streams[i].ContainsKey("index"))
					info.Streams[i].Index = ((int)streams[i]["index"]).ToString();

				if (streams[i].ContainsKey("r_frame_rate")) {
					var stringFrameRate = (string)streams[i]["r_frame_rate"];
					if (stringFrameRate.Contains('/')) {
						var split = stringFrameRate.Split('/');
						if (split.Length == 2 && int.TryParse(split[0], out var firstRate) && int.TryParse(split[1], out var secondRate))
							info.Streams[i].FrameRate = (firstRate > 0 && secondRate > 0) ? firstRate / (float)secondRate : -1f;
					}
				}

			}

			return info;
		}
	}
}
