# Leaderboard
Compatible with Unity 4 and later

## Setup
- Import LeaderboardManager.cs
- Attach it to a gameobject which will never be destroyed

## Inspector Variables
**Results Per Page**
Controls how many leaderboard rows will be contained in a single leaderboard request. Serverside caching relies on this variable so it should not be changed at runtime!

**Debug Mode**
This will log messages to the console when callbacks are triggered as well as logging of the URL which is being queried with your requests.

**Treat No Results As Error**
Tick this to make 'no results' trigger an error response rather than a successful one. It might be useful if you want to display a message about no leaderboard scores rather than showing a blank leaderboard. (although you can still do both with either option, it just depends how you want to structure your code)

## Scripting
### Check if a leaderboard is ready
```c#
if(LeaderboardManager.IsLeaderboardReady("Mode_1_Mission_4_AllTime")){
  Debug.Log("Leaderboard for mode 1, mission 4 with scores from all time is ready!");
}```
Returns a bool stating whether the requested leaderboard with the input id is ready. (Note that leaderboards are set as not ready when re-requesting their data. They'll be ready again once the request is complete)

### Check if a leaderboard had an error
```c#
if(LeaderboardManager.IsLeaderboardError("Mode_1_Mission_4_AllTime")){
  Debug.Log("Leaderboard for mode 1, mission 4 with scores from all time had an error!");
}```
Returns a bool stating whether the requested leaderboard with the input id had an error. (Set to false as soon as you re-request a new leaderboard. It'll be true again as soon as the request encounters an error - this and being ready can not be true at the same time)

### Check if a leaderboard request is active
```c#
if(LeaderboardManager.IsLeaderboardActive("Mode_1_Mission_4_AllTime")){
  Debug.Log("Leaderboard for mode 1, mission 4 with scores from all time is still requesting data!");
}```
Returns a bool stating whether the request is still processing

### Check if leaderboard data is currently being submitted
```c#
if(LeaderboardManager.IsSubmitActive()){
  Debug.Log("Leaderboard is still submitting!");
}
```
Returns a bool of whether a leaderboard submission is currently active. (Only 1 leaderboard submission can be active at once so be patient with requests!)

###

### Quick references
LeaderboardManager.IsLeaderboardReady(string leaderboardId)
LeaderboardManager.IsLeaderboardError(string leaderboardId)
LeaderboardManager.IsLeaderboardActive(string leaderboardId)

LeaderboardManager.IsSubmitActive()

LeaderboardManager.IsRankReady(string leaderboardId)
LeaderboardManager.IsRankError(string leaderboardId)
LeaderboardManager.IsRankActive(string leaderboardId)

LeaderboardManager.GetLeaderboard(string leaderboardId)
LeaderboardManager.GetRank(string leaderboardId)

LeaderboardManager.GetLeaderboardData(string leaderboardId, string deviceId = "", TimePeriod timePeriod = TimePeriod.AllTime, int pageNum = 0)
LeaderboardManager.GetLeaderboardRankData(string leaderboardId, int score, TimePeriod timePeriod = TimePeriod.AllTime, string deviceId = "")
LeaderboardManager.SetLeaderboardData(string leaderboardId, string deviceId, string nickname, int score)
