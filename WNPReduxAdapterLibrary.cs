using System.Collections.Concurrent;
using System.Collections.Generic;
using System;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Linq;

/*
 * TODO:
 * State: 0 for stopped, 1 for playing, and 2 for paused.
 * Status: 0 for inactive (player closed) and 1 for active (player open).
 */

namespace WNPReduxAdapterLibrary {
  public class MediaInfo {
    private string _Title { get; set; }
    private StateMode _State { get; set; }

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
    public int Volume { get; set; }
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
    public void TogglePlaying() { WNPRedux.SendMessage($"{Events.TOGGLE_PLAYING}"); }
    public void Next() { WNPRedux.SendMessage($"{Events.NEXT}"); }
    public void Previous() { WNPRedux.SendMessage($"{Events.PREVIOUS}"); }
    public void SetPositionSeconds(int seconds) {
      int positionInSeconds = seconds;
      if (positionInSeconds < 0) positionInSeconds = 0;
      if (positionInSeconds > WNPRedux.mediaInfo.DurationSeconds) positionInSeconds = WNPRedux.mediaInfo.DurationSeconds;
      double positionInPercent = (double)positionInSeconds / WNPRedux.mediaInfo.DurationSeconds;

      WNPRedux.SendMessage($"{Events.SET_POSITION} {positionInSeconds}:{positionInPercent}");
    }
    // Lots of wrappers for `setPositionSeconds`
    public void RevertPositionSeconds(int seconds) {
      SetPositionSeconds(WNPRedux.mediaInfo.PositionSeconds - seconds);
    }
    public void ForwardPositionSeconds(int seconds) {
      SetPositionSeconds(WNPRedux.mediaInfo.PositionSeconds + seconds);
    }
    public void RevertPositionPercent(double percent) {
      int seconds = (int)Math.Round(percent / 100 * WNPRedux.mediaInfo.DurationSeconds);
      SetPositionSeconds(WNPRedux.mediaInfo.PositionSeconds - seconds);
    }
    public void ForwardPositionPercent(double percent) {
      int seconds = (int)Math.Round(percent / 100 * WNPRedux.mediaInfo.DurationSeconds);
      SetPositionSeconds(WNPRedux.mediaInfo.PositionSeconds + seconds);
    }
    public void SetPositionPercent(double percent) {
      int seconds = (int)Math.Round(percent / 100 * WNPRedux.mediaInfo.DurationSeconds);
      SetPositionSeconds(seconds);
    }
    // Volume is 0-100
    public void SetVolume(int volume) {
      int newVolume = volume;
      if (volume < 0) newVolume = 0;
      if (volume > 100) newVolume = 100;
      WNPRedux.SendMessage($"{Events.SET_VOLUME} {newVolume}");
    }
    // You can't directly set a repeat state, but you can toggle through them
    public void ToggleRepeat() { WNPRedux.SendMessage($"{Events.TOGGLE_REPEAT}"); }
    public void ToggleShuffle() { WNPRedux.SendMessage($"{Events.TOGGLE_SHUFFLE}"); }
    public void ToggleThumbsUp() { WNPRedux.SendMessage($"{Events.TOGGLE_THUMBS_UP}"); }
    public void ToggleThumbsDown() { WNPRedux.SendMessage($"{Events.TOGGLE_THUMBS_DOWN}"); }
    /* (0-5) (0 = no rating) (1-2 = thumbs down) (3-5 = thumbs up) */
    public void SetRating(int rating) { WNPRedux.SendMessage($"{Events.SET_RATING} {rating}"); }
  }

  public class WNPRedux {
    // mediaInfo is what previously was 'displayedMusicInfo'
    public static MediaInfo mediaInfo = new MediaInfo();
    public static MediaEvents mediaEvents = new MediaEvents();
    private static WebSocketServer ws = null;
    public static int clients = 0;

    // Dictionary of media info, key is websocket client id
    private static readonly ConcurrentDictionary<string, MediaInfo>
        mediaInfoDictionary = new ConcurrentDictionary<string, MediaInfo>();

    public enum LogType { DEBUG, WARNING, ERROR };
    public static Action<LogType, string> logger;
    public static void Initialize(int port, Action<LogType, string> _logger) {
      try {
        if (ws != null) return;
        ws = new WebSocketServer(port);
        ws.AddWebSocketService<WNPReduxWebSocket>("/");
        ws.Start();
        logger = _logger;
        mediaInfo = new MediaInfo();
        mediaEvents = new MediaEvents();
      } catch (Exception e) {
        Log(LogType.ERROR, "WNPRedux: Failed to start websocket");
        Log(LogType.DEBUG, $"WNPRedux Trace: {e}");
      }
    }

    public static void SendMessage(string message) {
      if (ws == null || mediaInfo.WebSocketID.Length == 0) return;
      ws.WebSocketServices.TryGetServiceHost("/", out WebSocketServiceHost host);
      host.Sessions.SendTo(message, mediaInfo.WebSocketID);
    }

    public static void Close() {
      if (ws != null) {
        ws.Stop();
        ws = null;
      }
      mediaInfo = new MediaInfo();
      mediaEvents = new MediaEvents();
    }

    private static void Log(LogType type, string message) {
      logger(type, message);
      // TODO: logging, don't allow repeated log spam
      // TODO: log type is no longer a thing, since we don't use rainmeter
      // logging (this is a library)
    }

    public static void UpdateMediaInfo() {
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
        Log(LogType.ERROR, "Error finding new media info to display");
        Log(LogType.DEBUG, e.ToString());
      }
    }

    public class WNPReduxWebSocket : WebSocketBehavior {
      private int ConvertTimeStringToSeconds(string time) {
        string[] durArr = time.Split(':');

        // Duration will always have seconds and minutes
        int durSec = Convert.ToInt16(durArr[durArr.Length - 1]);
        int durMin = durArr.Length > 1 ? Convert.ToInt16(durArr[durArr.Length - 2]) * 60 : 0;
        int durHour = durArr.Length > 2 ? Convert.ToInt16(durArr[durArr.Length - 3]) * 60 * 60 : 0;

        return durHour + durMin + durSec;
      }

      protected override void OnMessage(MessageEventArgs arg) {
        try {
          string type = arg.Data.Substring(0, arg.Data.IndexOf(":")).ToUpper();
          string info = arg.Data.Substring(arg.Data.IndexOf(":") + 1);

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
          else if (type == "ERROR") Log(LogType.ERROR, $"WNP Redux Error: {info}");
          else if (type == "ERRORDEBUG") Log(LogType.DEBUG, $"WNP Redux Debug Error: {info}");
          else Log(LogType.WARNING, $"Unknown message type: {type}");

          if (type != "POSITION" && currentMediaInfo.Title != "")
            UpdateMediaInfo();
        } catch (Exception e) {
          Log(LogType.ERROR, "Error parsing data from WebNowPlaying Redux");
          Log(LogType.DEBUG, e.ToString());
        }
      }

      protected override void OnOpen() {
        base.OnOpen();
        clients++;

        // This gets the final assembly it's built into, so we can send this from in here
        System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
        System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
        // Update WNPRLIB_REVISION when we update something that might break communication compatibility
        Sessions.Broadcast($"ADAPTER_VERSION {fvi.FileVersion};WNPRLIB_REVISION 1");

        mediaInfoDictionary.GetOrAdd(ID, new MediaInfo());
      }

      protected override void OnClose(CloseEventArgs e) {
        base.OnClose(e);
        clients--;

        // If removing the last index in the update list and there is one it download album art
        mediaInfoDictionary.TryRemove(ID, out MediaInfo temp);
        if (mediaInfo.WebSocketID == temp.WebSocketID)
          UpdateMediaInfo();
      }
    }
  }
}
