# Leaderboard
Compatible and tested with Unity 4, Unity 5 and Unity 2017

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
}
```
Returns a bool stating whether the requested leaderboard with the input id is ready. (Note that leaderboards are set as not ready when re-requesting their data. They'll be ready again once the request is complete)

---

### Check if a leaderboard had an error
```c#
if(LeaderboardManager.IsLeaderboardError("Mode_1_Mission_4_AllTime")){
  Debug.Log("Leaderboard for mode 1, mission 4 with scores from all time had an error!");
}
```
Returns a bool stating whether the requested leaderboard with the input id had an error. (Set to false as soon as you re-request a new leaderboard. It'll be true again as soon as the request encounters an error - this and being ready can not be true at the same time)

---

### Check if a leaderboard request is active
```c#
if(LeaderboardManager.IsLeaderboardActive("Mode_1_Mission_4_AllTime")){
  Debug.Log("Leaderboard for mode 1, mission 4 with scores from all time is still requesting data!");
}
```
Returns a bool stating whether the request is still processing

---

### Check if leaderboard data is currently being submitted
```c#
if(LeaderboardManager.IsSubmitActive()){
  Debug.Log("Leaderboard is still submitting!");
}
```
Returns a bool of whether a leaderboard submission is currently active. (Only 1 leaderboard submission can be active at once so be patient with requests!)

---

### Check if rank data is ready
```c#
if(LeaderboardManager.IsRankReady("Mode_1_Mission_4_AllTime")){
  Debug.Log("Rank for mode 1, mission 4 with scores from all time is ready!");
}
```
Returns a bool stating whether the requested rank data is ready (Set to false as soon as you re-request the rank data again. It'll be true again as soon as the request is complete)

---

### Check if rank data had an error
```c#
if(LeaderboardManager.IsRankError("Mode_1_Mission_4_AllTime")){
  Debug.Log("Rank for mode 1, mission 4 with scores from all time is ready!");
}
```
Returns a bool stating whether the requested rank data with the input id has an erro (Set to false as soon as the re-request the rank data again)

---

### Check if rank request is still active
```c#
if(LeaderboardManager.IsRankActive("Mode_1_Mission_4_AllTime")){
  Debug.Log("Rank for mode 1, mission 4 with scores from all time is ready!");
}
```
Returns a bool stating whether the requested rank data is still processing

---

### Get a leaderboard once it is ready (the ready callback also includes this data as a parameter)
```c#
int curPage = 0;
LeaderboardResponse activeLeaderboard = LeaderboardManager.GetLeaderboard("Mode_1_Mission_4_AllTime");

Debug.Log("In this leaderboard there's " + activeLeaderboard.Count() + " scores");
for(int i=0;i < activeLeaderboard.Count();i++)
{
  LeaderboardStorage leaderboardRow = activeLeaderboard.Get(i);
  Debug.Log("Rank: #" + ((curPage * LeaderboardManager.resultsPerPage) + i)"Name: " + leaderboardRow.nickname + ", Score: " + leaderboardRow.score);
}
```
Get a leaderboard by id once the request for it has completed.
Calling this function when the leaderboard hasn't been requested yet will return null
Calling this function when the leaderboard has been requested but isn't ready will either contain data from a previous leaderboard request or the data of a blank leaderboard

---

### Get a leaderboard rank once it is ready (the ready callback also includes this data as a parameter)
```c#
RankResponse activeRank = LeaderboardManager.GetRank("Mode_1_Mission_4_AllTime");

Debug.Log("All time rank: #" + activeRank.response);
```
This rank is used to show what rank the player would be placed into the leaderboard if they were to submit their score.
Calling this function when the rank hasn't been requested yet will return null
Calling this function when the rank has been requested but isn't ready yet will either contain data from a previous rank request or the data will be blank (empty string rank)

---

### Send a request to get leaderboard data
```c#

```

---

### Send a request to get rank data
```c#

```

---

### Send a request to add a score to the leaderboard
```c#

```

---

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
