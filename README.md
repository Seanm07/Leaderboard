# Sean's GamePickle Leaderboard
Compatible and tested with Unity 4, Unity 5 and Unity 2017.
It works by sending GET requests to our server which runs mysql queries which are cached with memcaching and flushed upon submitting new scores.

Wrote this as fast as possible so if there's any important information missing just ask us and we can help directly or add additional information to this readme.

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
LeaderboardManager.GetLeaderboardData("Mode_1_Mission_4_AllTime", "", TimePeriod.AllTime, 0);
```
The above function call will send a request for the first page of leaderboard rows from all time for mode 1, mission 4 for example.

As we left the deviceId field as an empty string it will return results from all devices which submitted a score into this leaderboard. However if we only want results for a certain device we simply set the deviceId here and only leaderboard submissions from that user will be shown.

We can also change the time period to TimePeriod.Today if we only want the past 24 hours of results to be returned.

When choosing your leaderboardIds make sure to keep them unique if you're wanting to display multiple leaderboards at once. In this example I included the time period in the leaderboardId because I wanted to have TimePeriod.AllTime and TimePeriod.Today working alongside each other as tabs on the leaderboard.

If you wanted to load multiple pages of leaderboards at once you might also want to include the page number in the leaderboardId too.

Leaderboard results are ordered descending in rank, so the highest score is returned first and lowest last.

---

### Send a request to get rank data
```c#
LeaderboardManager.GetRankData("Mode_1_Mission_4_AllTime", Mathf.RoundToInt(myTimedScore * 100f), "", TimePeriod.AllTime);
```
The rank works similarly to the leaderboard requests so read that first.

This function returns what rank a score would be if it was in a leaderboard without needing to return every page of the leaderboard to find where the score would fit in.

Leaderboard scores are stored as ints and ranked by highest value to lowest value. In this example the miliseconds are taken into consideration in the score so we're multiplying the final time by 100 and rounding it to an int for storage.

---

### Send a request to add a score to the leaderboard
```c#
LeaderboardManager.SetLeaderboardData("Mode_1_Mission_4_AllTime", GoogleAnalytics.Instance.clientID, "Cool Guy 123", Mathf.RoundToInt(myTimedScore * 100f));
```
Submits a score into the leaderboard, note that a device id is required for this function.

The clientID must be unique to the player, if your game has permissions to grab the player email then go ahead and use that if you want to allow leaderboard submission per user rather than per device. But for most uses the clientID we generate in the GoogleAnalytics script is good enough for this.

The nickname is just a name for the player to identifier theirselves and let others see. The nickname is compared against a filter list when being submitted to the server and any bad words are converted to special chars randomly.

---

### Callbacks
```c#
void OnEnable()
{
  // Triggered when trying to make a request when there is no internet connection
  LeaderboardManager.OnSubmitConnectionFailed += SubmitConnectionFailed;
  LeaderboardManager.OnLeaderboardConnectionFailed += LeaderboardConnectionFailed;
  LeaderboardManager.OnRankConnectionFailed += RankConnectionFailed;
  
  // Triggered when a request is already active
  // Note: Only 1 submission can be sent at once, everything else allows unlimited but you can only re-request the same leaderboardId when the previous request on that leaderboardId finishes
  LeaderboardManager.OnSubmitAlreadyPending += SubmitAlreadyPending;
  LeaderboardManager.OnLeaderbopardAlreadyPending += LeaderboardAlreadyPending;
  LeaderboardManager.OnRankAlreadyPending += RankAlreadyPending;
  
  // Triggered when a request fails, can be missing a parameter, server error or something else
  LeaderboardManager.OnSubmitRequestFailed += SubmitRequestFailed;
  LeaderboardManager.OnLeaderboardRequestFailed += LeaderboardRequestFailed;
  LeaderboardManager.OnRankRequestFailed += RankRequestFailed;
  
  // Triggered when a data request finishes
  LeaderboardManager.OnSubmitDone += SubmitDone;
  LeaderboardManager.OnLeaderboardDone += LeaderboardDone;
  LeaderboardManager.OnRankDone += RankDone;
}

void OnDisable()
{
  LeaderboardManager.OnSubmitConnectionFailed -= SubmitConnectionFailed;
  LeaderboardManager.OnLeaderboardConnectionFailed -= LeaderboardConnectionFailed;
  LeaderboardManager.OnRankConnectionFailed -= RankConnectionFailed;
  
  LeaderboardManager.OnSubmitAlreadyPending -= SubmitAlreadyPending;
  LeaderboardManager.OnLeaderbopardAlreadyPending -= LeaderboardAlreadyPending;
  LeaderboardManager.OnRankAlreadyPending -= RankAlreadyPending;
  
  LeaderboardManager.OnSubmitRequestFailed -= SubmitRequestFailed;
  LeaderboardManager.OnLeaderboardRequestFailed -= LeaderboardRequestFailed;
  LeaderboardManager.OnRankRequestFailed -= RankRequestFailed;
  
  LeaderboardManager.OnSubmitDone -= SubmitDone;
  LeaderboardManager.OnLeaderboardDone -= LeaderboardDone;
  LeaderboardManager.OnRankDone -= RankDone;
}

void SubmitConnectionFailed(string leaderboardId){}
void LeaderboardConnectionFailed(string leaderboardId){}
void RankConnectionFailed(string leaderboardId){}

void SubmitAlreadyPending(string leaderboardId){}
void LeaderboardAlreadyPending(string leaderboardId){}
void RankAlreadyPending(string leaderboardId){}

void SubmitRequestFailed(string leaderboardId, string errorMessage){}
void LeaderboardRequestFailed(string leaderboardId, string errorMessage){}
void RankRequestFailed(string leaderboardId, string errorMessage){}

void SubmitDone(string leaderboardId){}
void LeaderboardDone(string leaderboardId, LeaderboardResponse leaderboardData){}
void RankDone(string leaderboardId, RankResponse rankData){}
```
I've listed all callbacks being used here as an example, only link up which callbacks you actually need. (You may actually need them all too, that's fine)

Also it's useful to know that the callbacks from OnLeaderboardDone and OnRankDone also include the LeaderboardResponse and RankResponse data, so you don't need to manually go and call GetLeaderboardData(..) within these functions!

---

## Quick references
```c#
LeaderboardManager.IsLeaderboardReady(string leaderboardId);
LeaderboardManager.IsLeaderboardError(string leaderboardId);
LeaderboardManager.IsLeaderboardActive(string leaderboardId);

LeaderboardManager.IsSubmitActive();

LeaderboardManager.IsRankReady(string leaderboardId);
LeaderboardManager.IsRankError(string leaderboardId);
LeaderboardManager.IsRankActive(string leaderboardId);

LeaderboardManager.GetLeaderboard(string leaderboardId);
LeaderboardManager.GetRank(string leaderboardId);

LeaderboardManager.GetLeaderboardData(string leaderboardId, string deviceId = "", TimePeriod timePeriod = TimePeriod.AllTime, int pageNum = 0);
LeaderboardManager.GetLeaderboardRankData(string leaderboardId, int score, TimePeriod timePeriod = TimePeriod.AllTime, string deviceId = "");
LeaderboardManager.SetLeaderboardData(string leaderboardId, string deviceId, string nickname, int score);
```
