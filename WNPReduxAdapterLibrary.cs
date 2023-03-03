#pragma warning disable 1591
using System.Collections.Concurrent;
using System.Collections.Generic;
using System;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Linq;
using System.Threading.Tasks;

namespace WNPReduxAdapterLibrary {
  public class WNPRedux {
    /// <summary>
    /// Info about the currently playing media.
    /// </summary>
    public static MediaInfo mediaInfo = new MediaInfo();
    /// <summary>
    /// Events to interact with currently playing media.
    /// </summary>
    public static MediaEvents mediaEvents = new MediaEvents();
    private static WebSocketServer ws = null;
    /// <summary>
    /// Number of connected clients.
    /// </summary>
    public static int clients = 0;

    /// <summary>
    /// Dictionary of all media info by all connected clients, keyed by the WebsocketID
    /// </summary>
    private static readonly ConcurrentDictionary<string, MediaInfo>
        mediaInfoDictionary = new ConcurrentDictionary<string, MediaInfo>();

    public enum LogType { DEBUG, WARNING, ERROR };
    private static Action<LogType, string> _logger;
    private static bool _throttleLogs = false;
    private static string adapterVersion = "";
    /// <summary>
    /// Opens the WebSocket if it isn't already opened.
    /// <returns>No return type</returns>
    /// <example>
    /// <code>
    /// void Logger(WNPRedux.LogType type, string message) {
    ///   Console.WriteLine($"{type}: {message}");
    /// }
    /// WNPRedux.Initialize(1234, "1.0.0", logger);
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="port">WebSocket Port</param>
    /// <param name="version">Adapter Version (major.minor.patch)</param>
    /// <param name="logger">Custom logger</param>
    /// <param name="throttleLogs">Prevent the same log message being logged more than once per 30 seconds</param>
    public static void Initialize(int port, string version, Action<LogType, string> logger, bool throttleLogs = false) {
      try {
        if (ws != null) return;
        adapterVersion = version;
        _logger = logger;
        _throttleLogs = throttleLogs;
        mediaInfo = new MediaInfo();
        mediaEvents = new MediaEvents();
        ws = new WebSocketServer(port);
        ws.AddWebSocketService<WNPReduxWebSocket>("/");
        ws.Start();
      } catch (Exception e) {
        Log(LogType.ERROR, "WNPRedux: Failed to start websocket");
        Log(LogType.DEBUG, $"WNPRedux Trace: {e}");
      }
    }

    private static void SendMessage(string message) {
      if (ws == null || mediaInfo.WebSocketID.Length == 0) return;
      ws.WebSocketServices.TryGetServiceHost("/", out WebSocketServiceHost host);
      host.Sessions.SendTo(message, mediaInfo.WebSocketID);
    }

    /// <summary>
    /// Closes the WebSocket if it's opened.
    /// </summary>
    public static void Close() {
      if (ws != null) {
        ws.Stop();
        ws = null;
      }
      _throttleLogs = false;
      mediaInfo = new MediaInfo();
      mediaEvents = new MediaEvents();
    }

    private static readonly object _lock = new object();
    private static readonly Dictionary<string, int> _logCounts = new Dictionary<string, int>();
    private static readonly int _maxLogCount = 1;
    private static readonly TimeSpan _logResetTime = TimeSpan.FromSeconds(30);
    private static void Log(LogType type, string message) {
      if (!_throttleLogs) {
        _logger(type, message);
        return;
      }

      lock (_lock) {
        DateTime currentTime = DateTime.Now;
        string typeMessage = $"{type} {message}";

        if (!_logCounts.TryGetValue(typeMessage, out int logCount)) logCount = 0;

        if (logCount < _maxLogCount) {
          _logger(type, message);
          _logCounts[typeMessage] = logCount + 1;
          Task.Run(async () => {
            await Task.Delay(_logResetTime);
            lock (_lock) {
              _logCounts.Remove(typeMessage);
            }
          });
        }
      }
    }

    private static void UpdateMediaInfo() {
      try {
        var iterableDictionary = mediaInfoDictionary.OrderByDescending(key => key.Value.Timestamp);
        bool suitableMatch = false;

        foreach (KeyValuePair<string, MediaInfo> item in iterableDictionary) {
          // No need to check title since timestamp is only set when title is set
          if (item.Value.State == MediaInfo.StateMode.PLAYING && item.Value.Volume >= 1) {
            mediaInfo = item.Value;
            suitableMatch = true;
            // If match found break early which should be always very early
            break;
          }
        }

        if (!suitableMatch) {
          MediaInfo fallbackInfo = iterableDictionary.FirstOrDefault().Value;
          if (fallbackInfo == null) fallbackInfo = new MediaInfo();
          mediaInfo = fallbackInfo;
        }
      } catch (Exception e) {
        Log(LogType.ERROR, "WNPRedux: Error finding new media info to display");
        Log(LogType.DEBUG, $"WNPRedux Trace: {e}");
      }
    }

    public class MediaInfo {
      private string _Title { get; set; }
      private StateMode _State { get; set; }
      private int _Volume { get; set; }

      public MediaInfo() {
        _State = StateMode.STOPPED;
        WebSocketID = "";
        Player = "";
        _Title = "";
        Artist = "";
        Album = "";
        CoverUrl = "";
        Duration = "0:00";
        DurationSeconds = 0;
        Position = "0:00";
        PositionSeconds = 0;
        PositionPercent = 0;
        Volume = 100;
        Rating = 0;
        RepeatState = RepeatMode.NONE;
        Shuffle = false;
        Timestamp = 0;
      }

      public enum StateMode { STOPPED, PLAYING, PAUSED }
      public enum RepeatMode { NONE, ONE, ALL }
      public StateMode State {
        get { return _State; }
        set {
          _State = value;
          Timestamp = DateTime.Now.Ticks / (decimal)TimeSpan.TicksPerMillisecond;
        }
      }
      public string WebSocketID { get; set; }
      public string Player { get; set; }
      public string Title {
        get { return _Title; }
        set {
          _Title = value;
          if (value != "") {
            Timestamp = DateTime.Now.Ticks / (decimal)TimeSpan.TicksPerMillisecond;
          } else {
            Timestamp = 0;
          }
        }
      }
      public string Artist { get; set; }
      public string Album { get; set; }
      public string CoverUrl { get; set; }
      public string Duration { get; set; }
      public int DurationSeconds { get; set; }
      public string Position { get; set; }
      public int PositionSeconds { get; set; }
      public double PositionPercent { get; set; }
      public int Volume {
        get { return _Volume; }
        set {
          _Volume = value;
          Timestamp = DateTime.Now.Ticks / (decimal)TimeSpan.TicksPerMillisecond;
        }
      }
      public int Rating { get; set; }
      public RepeatMode RepeatState { get; set; }
      public bool Shuffle { get; set; }
      public decimal Timestamp { get; set; }
    }

    public class MediaEvents {
      private enum Events {
        TOGGLE_PLAYING,
        NEXT,
        PREVIOUS,
        SET_POSITION,
        SET_VOLUME,
        TOGGLE_REPEAT,
        TOGGLE_SHUFFLE,
        TOGGLE_THUMBS_UP,
        TOGGLE_THUMBS_DOWN,
        SET_RATING
      }
      /// <summary>
      /// Toggles the playing state of the current media.
      /// </summary>
      public void TogglePlaying() { SendMessage($"{Events.TOGGLE_PLAYING}"); }
      /// <summary>
      /// Skips to the next media/section if supported.
      /// </summary>
      public void Next() { SendMessage($"{Events.NEXT}"); }
      /// <summary>
      /// Skips to the previous media/section if supported.
      /// </summary>
      public void Previous() { SendMessage($"{Events.PREVIOUS}"); }
      /// <summary>
      /// Sets the current medias playback progress in seconds
      /// </summary>
      /// <param name="seconds"></param>.
      public void SetPositionSeconds(int seconds) {
        int positionInSeconds = seconds;
        if (positionInSeconds < 0) positionInSeconds = 0;
        if (positionInSeconds > mediaInfo.DurationSeconds) positionInSeconds = mediaInfo.DurationSeconds;
        double positionInPercent = (double)positionInSeconds / mediaInfo.DurationSeconds;

        SendMessage($"{Events.SET_POSITION} {positionInSeconds}:{positionInPercent}");
      }
      /// <summary>
      /// Reverts the current medias playback progress by x seconds
      /// </summary>
      /// <param name="seconds"></param>.
      public void RevertPositionSeconds(int seconds) {
        SetPositionSeconds(mediaInfo.PositionSeconds - seconds);
      }
      /// <summary>
      /// Forwards the current medias playback progress by x seconds
      /// </summary>
      /// <param name="seconds"></param>.
      public void ForwardPositionSeconds(int seconds) {
        SetPositionSeconds(mediaInfo.PositionSeconds + seconds);
      }
      /// <summary>
      /// Sets the current medias playback progress in percent.
      /// </summary>
      /// <param name="percent"></param>
      public void SetPositionPercent(double percent) {
        int seconds = (int)Math.Round(percent / 100 * mediaInfo.DurationSeconds);
        SetPositionSeconds(seconds);
      }
      /// <summary>
      /// Reverts the current medias playback progress by x percent
      /// </summary>
      /// <param name="percent"></param>.
      public void RevertPositionPercent(double percent) {
        int seconds = (int)Math.Round(percent / 100 * mediaInfo.DurationSeconds);
        SetPositionSeconds(mediaInfo.PositionSeconds - seconds);
      }
      /// <summary>
      /// Forwards the current medias playback progress by x percent
      /// </summary>
      /// <param name="percent"></param>.
      public void ForwardPositionPercent(double percent) {
        int seconds = (int)Math.Round(percent / 100 * mediaInfo.DurationSeconds);
        SetPositionSeconds(mediaInfo.PositionSeconds + seconds);
      }
      /// <summary>
      /// Sets the current medias volume level
      /// </summary>
      /// <param name="volume">Number from 0-100</param>
      public void SetVolume(int volume) {
        int newVolume = volume;
        if (volume < 0) newVolume = 0;
        if (volume > 100) newVolume = 100;
        SendMessage($"{Events.SET_VOLUME} {newVolume}");
      }
      /// <summary>
      /// Toggles the current medias repeat state if supported.
      /// </summary>
      public void ToggleRepeat() { SendMessage($"{Events.TOGGLE_REPEAT}"); }
      /// <summary>
      /// Toggles the current medias shuffle state if supported.
      /// </summary>
      public void ToggleShuffle() { SendMessage($"{Events.TOGGLE_SHUFFLE}"); }
      /// <summary>
      /// Toggles thumbs up or similar on the current media if supported.
      /// </summary>
      public void ToggleThumbsUp() { SendMessage($"{Events.TOGGLE_THUMBS_UP}"); }
      /// <summary>
      /// Toggles thumbs down or similar on the current media if supported.
      /// </summary>
      public void ToggleThumbsDown() { SendMessage($"{Events.TOGGLE_THUMBS_DOWN}"); }
      /// <summary>
      /// Sets the rating from 0-5 on websites that support it.
      /// Falls back to:
      /// 0 = no rating
      /// 1-2 = Thumbs Down
      /// 3-5 = Thumbs Up
      /// </summary>
      /// <param name="rating">Number from 0-5</param>
      public void SetRating(int rating) { SendMessage($"{Events.SET_RATING} {rating}"); }
    }

    private class WNPReduxWebSocket : WebSocketBehavior {
      private int ConvertTimeStringToSeconds(string time) {
        string[] durArr = time.Split(':');

        // Duration will always have seconds and minutes, but hours are optional
        int durSec = Convert.ToInt16(durArr[durArr.Length - 1]);
        int durMin = durArr.Length > 1 ? Convert.ToInt16(durArr[durArr.Length - 2]) * 60 : 0;
        int durHour = durArr.Length > 2 ? Convert.ToInt16(durArr[durArr.Length - 3]) * 60 * 60 : 0;

        return durHour + durMin + durSec;
      }

      protected override void OnMessage(MessageEventArgs arg) {
        try {
          string type = arg.Data.Substring(0, arg.Data.IndexOf(" ")).ToUpper();
          string info = arg.Data.Substring(arg.Data.IndexOf(" ") + 1);

          MediaInfo currentMediaInfo = new MediaInfo();
          if (!mediaInfoDictionary.TryGetValue(ID, out currentMediaInfo)) {
            // This should never happen but just in case do this to prevent getting an error
            currentMediaInfo = new MediaInfo();
            mediaInfoDictionary.GetOrAdd(ID, currentMediaInfo);
          }
          // I guess that TryGetValue can return an uninitialized value in some errors, so make sure it is good
          if (currentMediaInfo == null) {
            currentMediaInfo = new MediaInfo();
          }

          currentMediaInfo.WebSocketID = ID;

          if (type == "PLAYER") currentMediaInfo.Player = info;
          else if (type == "STATE") currentMediaInfo.State = (MediaInfo.StateMode)Enum.Parse(typeof(MediaInfo.StateMode), info);
          else if (type == "TITLE") currentMediaInfo.Title = info;
          else if (type == "ARTIST") currentMediaInfo.Artist = info;
          else if (type == "ALBUM") currentMediaInfo.Album = info;
          else if (type == "COVER") currentMediaInfo.CoverUrl = info;
          else if (type == "DURATION") {
            currentMediaInfo.Duration = info;
            currentMediaInfo.DurationSeconds = ConvertTimeStringToSeconds(info);
            // I guess set positionPercent to 0, because if duration changes, a new video is playing
            currentMediaInfo.PositionPercent = 0;
          } else if (type == "POSITION") {
            currentMediaInfo.Position = info;
            currentMediaInfo.PositionSeconds = ConvertTimeStringToSeconds(info);

            if (currentMediaInfo.DurationSeconds > 0) {
              currentMediaInfo.PositionPercent = (double)currentMediaInfo.PositionSeconds / currentMediaInfo.DurationSeconds * 100.0;
            } else {
              currentMediaInfo.PositionPercent = 100;
            }
          } else if (type == "VOLUME") currentMediaInfo.Volume = Convert.ToInt16(info);
          else if (type == "RATING") currentMediaInfo.Rating = Convert.ToInt16(info);
          else if (type == "REPEAT") currentMediaInfo.RepeatState = (MediaInfo.RepeatMode)Enum.Parse(typeof(MediaInfo.RepeatMode), info);
          else if (type == "SHUFFLE") currentMediaInfo.Shuffle = Boolean.Parse(info);
          else if (type == "ERROR") Log(LogType.ERROR, $"WNPRedux: Error from browser extension: {info}");
          else if (type == "ERRORDEBUG") Log(LogType.DEBUG, $"WNPRedux: Browser Error {info}");
          else Log(LogType.WARNING, $"Unknown message type: {type}");

          if (type != "POSITION" && currentMediaInfo.Title != "")
            UpdateMediaInfo();
        } catch (Exception e) {
          Log(LogType.ERROR, "WNPRedux: Error parsing data from WebNowPlaying Redux Browser Extension");
          Log(LogType.DEBUG, e.ToString());
        }
      }

      protected override void OnOpen() {
        base.OnOpen();
        clients++;

        // This used to get the version from the final assembly, but who knows what authors might do there.
        // We simply let them provide a version number on Initialization now.
        // Update WNPRLIB_REVISION when we update something that might break communication compatibility.
        Sessions.Broadcast($"ADAPTER_VERSION {adapterVersion};WNPRLIB_REVISION 1");

        mediaInfoDictionary.GetOrAdd(ID, new MediaInfo());
      }

      protected override void OnClose(CloseEventArgs e) {
        base.OnClose(e);
        clients--;

        mediaInfoDictionary.TryRemove(ID, out MediaInfo temp);
        if (mediaInfo.WebSocketID == temp.WebSocketID)
          UpdateMediaInfo();
      }
    }
  }
}

#pragma warning restore 1591
