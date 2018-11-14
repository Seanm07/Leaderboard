using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;
using UnityEngine.Networking;

#if !UNITY_5_3_OR_NEWER
	// Older versions of Unity don't support JsonUtility so fallback to using the SimpleJSON plugin
	using SimpleJSON;
#endif

[Serializable]
public class LeaderboardStorage {
	public string device_identifier;
	public string nickname;
	public int score;
	public long timestamp;

	public LeaderboardStorage(string inDeviceIdentifier, string inNickname, int inScore, long inTimestamp)
	{
		device_identifier = inDeviceIdentifier;
		nickname = inNickname;
		score = inScore;
		timestamp = inTimestamp;
	}
}

[Serializable]
public class LeaderboardResponse {
	public List<LeaderboardStorage> response = new List<LeaderboardStorage>();

	public bool isReady { get; set; }
	public bool isError { get; set; }
	public bool isActive { get; set; }

	public LeaderboardStorage Get(int id){ return response[id]; }
	public int Count(){ return response.Count; }
}

[Serializable]
public class RawCombinedLeaderboardResponse {
	public RawCombinedLeaderboardResponseData[] response;
}

[Serializable]
public class RawCombinedLeaderboardResponseData {
	public List<LeaderboardStorage> data;
	public string leaderboardId;
	public string pageNum;
}

[Serializable]
public class RawCombinedRankResponse {
	public RawCombinedRankResponseData[] response;
}

[Serializable]
public class RawCombinedRankResponseData {
	public string rankData;
	public string leaderboardId;
}

[Serializable]
public class RankResponse {
	public string response;

	public bool isReady { get; set; }
	public bool isError { get; set; }
	public bool isActive { get; set; }
}

[Serializable]
public class SubmissionCache {
	public string nickname;
	public int score;

	public SubmissionCache(string inNickname, int inScore)
	{
		nickname = inNickname;
		score = inScore;
	}
}

public class LeaderboardManager : MonoBehaviour {

	// Static script reference
	public static LeaderboardManager selfRef;

	// Leaderboard dictionary (the keys are the leaderboard identifiers)
	private Dictionary<string, LeaderboardResponse> leaderboardStorage = new Dictionary<string, LeaderboardResponse>();

	// Leaderboard submission dictionary (to keep track of which leaderboards are currently being submitted)
	private Dictionary<string, bool> leaderboardSubmissions = new Dictionary<string, bool>();

	// Keep a dictionary of submitted values so we don't need to submit the same scores again
	private Dictionary<string, SubmissionCache> leaderboardSubmissionCache = new Dictionary<string, SubmissionCache>();

	// Rank dictionary (the keys are the leaderboard identifiers)
	private Dictionary<string, RankResponse> rankStorage = new Dictionary<string, RankResponse>();

	// This is a fixed value because I have complicated serverside caching of queries which becomes messy if the results per page changes for each request
	public int resultsPerPage = 20;

	public string requestURL = "https://data.i6.com/datastore.php";

	public enum DebugModeTypes { None, RequestsOnly, Full }

	// When true extra messages will be logged which information about the requests
	public DebugModeTypes debugMode = DebugModeTypes.RequestsOnly;

	// Whether having no results should be treated as an error
	// Can be useful if you want to show a message instead of an empty leaderboard
	public bool treatNoResultsAsError = false;

	// Time periods for grabbing leaderboards (To add more time periods this also needs to be changed serverside as for caching purposes it's very strict)
	public enum TimePeriod { AllTime, PastMonth, PastWeek, Today }

	// Connection failed callback events (called when there's no internet connection)
	public static Action<string> OnSubmitConnectionFailed;
	public static Action<string> OnLeaderboardConnectionFailed;
	public static Action<string> OnRankConnectionFailed;

	// Connection busy callback events (called when a request is already running)
	public static Action<string> OnSubmitAlreadyPending;
	public static Action<string> OnLeaderboardAlreadyPending;
	public static Action<string> OnRankAlreadyPending;

	// Request failed callback events
	public static Action<string, string> OnSubmitRequestFailed;
	public static Action<string, string> OnLeaderboardRequestFailed;
	public static Action<string, string> OnRankRequestFailed;

	// Completed callback events
	public static Action<string> OnSubmitDone;
	public static Action<string, LeaderboardResponse> OnLeaderboardDone;
	public static Action<string, RankResponse> OnRankDone;

	#if UNITY_5_3_OR_NEWER
		private string packageName;
	#else
		// Older versions of Unity didn't have a way to get the bundle name via script..
		public string packageName = "com.pickle.CHANGE_THIS";
	#endif

	public int serverRequests = 0;
	public int cachedRequests = 0;

	void Awake()
	{
		#if UNITY_5_6_OR_NEWER
			packageName = Application.identifier;
		#elif UNITY_5_3_OR_NEWER
			packageName = Application.bundleIdentifier;
		#endif

		// Setup a static reference to we can reference non-static script members from static methods
		selfRef = (selfRef == null ? this : selfRef);
	}

	// Returns true if the last request made to this leaderboardId had finished
	public static bool IsLeaderboardReady(string leaderboardId)
	{
		LeaderboardResponse leaderboard = GetLeaderboard(leaderboardId);
		return leaderboard != null ? leaderboard.isReady : false;
	}

	// Returns true if the last request made to this leaderboardId had failed
	public static bool IsLeaderboardError(string leaderboardId)
	{
		LeaderboardResponse leaderboard = GetLeaderboard(leaderboardId);
		return leaderboard != null ? leaderboard.isError : false;
	}

	// Returns true if the last request made to this leaderboardId is still processing the request
	public static bool IsLeaderboardActive(string leaderboardId)
	{
		LeaderboardResponse leaderboard = GetLeaderboard(leaderboardId);
		return leaderboard != null ? leaderboard.isActive : false;
	}

	// Returns true if a submit request is still processing
	public static bool IsSubmitActive(string leaderboardId)
	{
		return selfRef.leaderboardSubmissions.ContainsKey (leaderboardId) && selfRef.leaderboardSubmissions [leaderboardId];
	}

	// Returns true if the last request made to this leaderboardId for the rank had finished
	public static bool IsRankReady(string leaderboardId)
	{
		RankResponse rank = GetRank(leaderboardId);
		return rank != null ? rank.isReady : false;
	}

	// Returns true if the last request made to this leaderboardId for the rank had failed
	public static bool IsRankError(string leaderboardId)
	{
		RankResponse rank = GetRank(leaderboardId);
		return rank != null ? rank.isError : false;
	}

	// Returns true if the last request made to this leaderboardId for the rank is still processing the request
	public static bool IsRankActive(string leaderboardId)
	{
		RankResponse rank = GetRank(leaderboardId);
		return rank != null ? rank.isActive : false;
	}

	private UnityWebRequest DoWebRequest(Dictionary<string, string> postData, bool sendValidationToken, bool sendChecksum)
	{
		// Add the always included platform and package_name to the post data
		postData.Add("platform", Application.platform.ToString());
		postData.Add("package_name", packageName);

		// If this request requires a validation token, add it to the post request
		if(sendValidationToken)
			postData.Add("token", GetSecurityToken());

		// We're done building the postData, now it will be turned into the post request (checksum will be added as a final post if sendChecksum is true)
		List<IMultipartFormSection> postRequest = new List<IMultipartFormSection>();

		// Add the post request values sent with the postData dictionary parameter
		foreach(KeyValuePair<string, string> post in postData)
			postRequest.Add(new MultipartFormDataSection(post.Key, post.Value, Encoding.UTF8, "multipart/form-data"));

		// If this request requires a checksum, add it to the post request
		if(sendChecksum){
			// The checksum allows us to validate that the requested URL matches the URL sent to the server
			string postDataString = string.Empty;

			// Create a string of all posted data (this is also done on the serverside for comparison)
			foreach(KeyValuePair<string, string> curPostData in postData)
				postDataString += curPostData.Value;

			postRequest.Add(new MultipartFormDataSection("checksum", GenerateChecksum(postDataString), Encoding.UTF8, "multipart/form-data"));

			// If we're in debug mode then we'll also add the checksum to the postData so it can be logged for debugging
			if(debugMode == DebugModeTypes.Full){
				Debug.Log("Decoded checksum data: " + postDataString);

				postData.Add("checksum", GenerateChecksum(postDataString));
			}
		}

		if(debugMode == DebugModeTypes.RequestsOnly)
			Debug.Log("[DEBUG] Sending request to: " + requestURL + " (type: " + postData["action"] + ", ref: " + postData["leaderboard"] + ")");

		if(debugMode == DebugModeTypes.Full){
			foreach(KeyValuePair<string, string> curPostData in postData)
				Debug.Log("[DEBUG] Post data '" + curPostData.Key + "' => '" + curPostData.Value + "'");
		}

		// Increment the server request counter
		serverRequests++;

		// Send the POST request to the URL with our built list of POST requests
		return UnityWebRequest.Post(requestURL, postRequest);
	}


	// Gets a leaderboard by the leaderboardId (returns null if there's no leaderboard data ready, or returns a blank LeaderboardResponse if the request hasn't finished yet)
	// Check with IsLeaderboardReady(..) first if you want to know the status, or wait for the callback
	public static LeaderboardResponse GetLeaderboard(string leaderboardId, string deviceId = "", TimePeriod timePeriod = TimePeriod.AllTime, int pageNum = 0)
	{
		string leaderboardStorageRefId = leaderboardId + deviceId + timePeriod + pageNum;

		return selfRef.leaderboardStorage.ContainsKey(leaderboardStorageRefId) ? selfRef.leaderboardStorage[leaderboardStorageRefId] : null;
	}

	// Gets a leaderboard rank by the leaderboard identifier (returns null if there's no leaderboard rank ready, or returns a blank RankResponse if the request is hasn't finished yet)
	// Check with IsRankReady(..) first if you want to know the status, or wait for the callback
	public static RankResponse GetRank(string leaderboardId, TimePeriod timePeriod = TimePeriod.AllTime, string deviceId = "")
	{
		string leaderboardRankStorageRefId = leaderboardId + timePeriod + deviceId;

		return selfRef.rankStorage.ContainsKey(leaderboardRankStorageRefId) ? selfRef.rankStorage[leaderboardRankStorageRefId] : null;
	}

	private void SetupRanksKey(string leaderboardRankStorageRefId)
	{
		// Add a rank key if one doesn't exist in the dictionary
		if(!selfRef.rankStorage.ContainsKey(leaderboardRankStorageRefId)){
			selfRef.rankStorage.Add(leaderboardRankStorageRefId, new RankResponse());

			// Reset the ready and error status
			selfRef.rankStorage[leaderboardRankStorageRefId].isReady = false;
			selfRef.rankStorage[leaderboardRankStorageRefId].isError = false;
			selfRef.rankStorage[leaderboardRankStorageRefId].isActive = false;
		}
	}

	private void SetupLeaderboardsKey(string leaderboardStorageRefId)
	{
		// Add a leaderboard key if one doesn't exist in the dictionary
		if(!selfRef.leaderboardStorage.ContainsKey(leaderboardStorageRefId)){
			selfRef.leaderboardStorage.Add(leaderboardStorageRefId, new LeaderboardResponse());

			// Reset the ready and error status
			selfRef.leaderboardStorage[leaderboardStorageRefId].isReady = false;
			selfRef.leaderboardStorage[leaderboardStorageRefId].isError = false;
			selfRef.leaderboardStorage[leaderboardStorageRefId].isActive = false;
		}
	}

	private void SetupSubmissionKey(string leaderboardId)
	{
		// Add a leaderboard submission key if one doesn't exist in the dictionary
		if(!selfRef.leaderboardSubmissions.ContainsKey(leaderboardId))
			selfRef.leaderboardSubmissions.Add(leaderboardId, false);
	}

	public static void GetCombinedLeaderboardData(string leaderboardKey, List<string> leaderboardId, List<TimePeriod> timePeriod, List<int> pageNum, string deviceId = "", bool forceRefresh = false)
	{
		// Start the leaderboard routine, this will send a server request for the data in the leaderboard
		selfRef.StartCoroutine(selfRef.DoGetCombinedLeaderboardData(leaderboardKey, leaderboardId, timePeriod, pageNum, deviceId, forceRefresh));
	}

	// Send a request for the leaderboard submissions within the requested leaderboard (only from this device if DeviceId is defined)
	public static void GetLeaderboardData(string leaderboardId, string deviceId = "", TimePeriod timePeriod = TimePeriod.AllTime, int pageNum = 0, bool forceRefresh = false)
	{
		if(!IsLeaderboardActive(leaderboardId)){
			// Create the leaderboard key in the dictionary if it doesn't exist
			selfRef.SetupLeaderboardsKey(leaderboardId + deviceId + timePeriod + pageNum);

			// Start the leaderboard routine, if the leaderboard isn't already cached or this is a force refresh request this will send a server request for the wanted data
			selfRef.StartCoroutine(selfRef.DoGetLeaderboardData(leaderboardId, deviceId, timePeriod, pageNum, forceRefresh));
		} else {
			if(selfRef.debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Get leaderboard for " + leaderboardId + " was already active");

			if(OnLeaderboardAlreadyPending != null)
				OnLeaderboardAlreadyPending.Invoke(leaderboardId);
		}
	}

	public static void GetCombinedLeaderboardRankData(string leaderboardRankKey, List<string> leaderboardId, List<int> score, List<TimePeriod> timePeriod, string deviceId = "", bool forceRefresh = false)
	{
		// Start the leaderboard routine, this will send a server request for the rank of the scores in the leaderboard
		selfRef.StartCoroutine(selfRef.DoGetCombinedLeaderboardRankData(leaderboardRankKey, leaderboardId, score, timePeriod, deviceId, forceRefresh));
	}

	// Send a request for what rank a score would be in the leaderboard
	public static void GetLeaderboardRankData(string leaderboardId, int score, TimePeriod timePeriod = TimePeriod.AllTime, string deviceId = "", bool forceRefresh = false)
	{
		if(!IsRankActive(leaderboardId)){
			// Create the leaderboard rank key in the dictionary if it doesn't exist
			selfRef.SetupRanksKey(leaderboardId + timePeriod + deviceId);

			// Start the leaderboard routine, this will send a server request for the rank a score has in the leaderboard
			selfRef.StartCoroutine(selfRef.DoGetLeaderboardRankData(leaderboardId, score, timePeriod, deviceId, forceRefresh));
		} else {
			if(selfRef.debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Get rank for " + leaderboardId + " was already active");

			if(OnRankAlreadyPending != null)
				OnRankAlreadyPending.Invoke(leaderboardId);
		}
	}

	// Submit a change in score
	public static void AdjustLeaderboardData(string leaderboardId, string deviceId, string nickname, int scoreChange)
	{
		if(!IsSubmitActive(leaderboardId)){
			// Create the leaderboard submission key in the dictionary if it doesn't exist
			selfRef.SetupSubmissionKey(leaderboardId);

			// Don't even bother starting the routine if we don't have a working internet connection
			if(Application.internetReachability != NetworkReachability.NotReachable){
				// Start the leaderboard routine, this will send a server request to submit the score
				selfRef.StartCoroutine(selfRef.DoAdjustLeaderboardData(leaderboardId, deviceId, nickname, scoreChange));
			} else {
				if(selfRef.debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Failed to adjust " + leaderboardId + "! No internet connection");

				if(OnSubmitConnectionFailed != null)
					OnSubmitConnectionFailed.Invoke(leaderboardId);
			}
		} else {
			if(selfRef.debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Failed to adjust " + leaderboardId + " as another submit was still active");

			if(OnSubmitAlreadyPending != null)
				OnSubmitAlreadyPending.Invoke(leaderboardId);
		}
	}

	// Submit a score to the requested leaderboard
	public static void SetLeaderboardData(string leaderboardId, string deviceId, string nickname, int score)
	{
		if(!IsSubmitActive(leaderboardId)){
			// Create the leaderboard submission key in the dictionary if it doesn't exist
			selfRef.SetupSubmissionKey(leaderboardId);

			// Don't even bother starting the routine if we don't have a working internet connection
			if(Application.internetReachability != NetworkReachability.NotReachable){
				// Start the leaderboard routine, this will send a server request to submit the score
				selfRef.StartCoroutine(selfRef.DoSetLeaderboardData(leaderboardId, deviceId, nickname, score));
			} else {
				if(selfRef.debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Failed to submit " + leaderboardId + "! No internet connection");

				if(OnSubmitConnectionFailed != null)
					OnSubmitConnectionFailed.Invoke(leaderboardId);
			}
		} else {
			if(selfRef.debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Failed to submit " + leaderboardId + " as another submit was still active");

			if(OnSubmitAlreadyPending != null)
				OnSubmitAlreadyPending.Invoke(leaderboardId);
		}
	}

	// Submit multiple leaderboard scores at once (all combined into a single web request)
	public static void SetCombinedLeaderboardData(string leaderboardSubmissionKey, List<string> leaderboardId, List<int> score, string deviceId, string nickname)
	{
		if(!IsSubmitActive(leaderboardSubmissionKey)){
			// Create the leaderboard submission key in the dictionary if it doesn't exist
			selfRef.SetupSubmissionKey(leaderboardSubmissionKey);

			// Don't even bother starting the routine if we don't have a working internet connection
			if(Application.internetReachability != NetworkReachability.NotReachable){
				// Start the leaderboard routine, this will send a server request to submit the score
				selfRef.StartCoroutine(selfRef.DoSetCombinedLeaderboardData(leaderboardSubmissionKey, leaderboardId, score, deviceId, nickname));
			} else {
				if(selfRef.debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Failed to combined submit " + leaderboardSubmissionKey + "! No internet connection");

				if(OnSubmitConnectionFailed != null)
					OnSubmitConnectionFailed.Invoke(leaderboardSubmissionKey);
			}
		} else {
			if(selfRef.debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Failed to combined submit " + leaderboardSubmissionKey + " as another submit was still active");

			if(OnSubmitAlreadyPending != null)
				OnSubmitAlreadyPending.Invoke(leaderboardSubmissionKey);
		}
	}

	public static void AdjustCombinedLeaderboardData(string leaderboardSubmissionKey, List<string> leaderboardId, List<int> scoreChange, string deviceId, string nickname)
	{
		if(!IsSubmitActive(leaderboardSubmissionKey)){
			// Create the leaderboard submission key in the dictionary if it doesn't exist
			selfRef.SetupSubmissionKey(leaderboardSubmissionKey);

			// Don't even bother starting the routine if we don't have a working internet connection
			if(Application.internetReachability != NetworkReachability.NotReachable){
				// Start the leaderboard routine, this will send a server request to submit the score
				selfRef.StartCoroutine(selfRef.DoAdjustCombinedLeaderboardData(leaderboardSubmissionKey, leaderboardId, scoreChange, deviceId, nickname));
			} else {
				if(selfRef.debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Failed to combined adjust " + leaderboardSubmissionKey + "! No internet connection");

				if(OnSubmitConnectionFailed != null)
					OnSubmitConnectionFailed.Invoke(leaderboardSubmissionKey);
			}
		} else {
			if(selfRef.debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Failed to combined adjust " + leaderboardSubmissionKey + " as another submit was still active");

			if(OnSubmitAlreadyPending != null)
				OnSubmitAlreadyPending.Invoke(leaderboardSubmissionKey);
		}
	}

	// Delete a score from the requested leaderboard (useful in cases where either the player or guild has been deleted/banned)
	public static void DeleteLeaderboardData(string leaderboardId, string deviceId)
	{
		if(!IsSubmitActive(leaderboardId)){
			// Create a the leaderboard submission key in the dictionary if it doesn't exist
			selfRef.SetupSubmissionKey(leaderboardId);

			// Don't even bother starting the routine if we don't have a working internet connection
			if(Application.internetReachability != NetworkReachability.NotReachable){
				// Start the leaderboard routine, this will send a server request to delete the score
				selfRef.StartCoroutine(selfRef.DoDeleteLeaderboardData(leaderboardId, deviceId));
			} else {
				if(selfRef.debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Failed to submit " + leaderboardId + "! No internet connection");

				if(OnSubmitConnectionFailed != null)
					OnSubmitConnectionFailed.Invoke(leaderboardId);
			}
		} else {
			if(selfRef.debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Failed to delete " + leaderboardId + " as another submit was still active");

			if(OnSubmitAlreadyPending != null)
				OnSubmitAlreadyPending.Invoke(leaderboardId);
		}
	}

	private IEnumerator DoGetCombinedLeaderboardRankData(string leaderboardRankKey, List<string> leaderboardId, List<int> score, List<TimePeriod> timePeriod, string deviceId = "", bool forceRefresh = false)
	{
		string[] leaderboardKeys = new string[leaderboardId.Count];

		string pendingQueryLeaderboardIds = "";
		string pendingQueryScores = "";
		string pendingQueryTimePeriods = "";

		for(int i=0;i < leaderboardId.Count;i++)
		{
			leaderboardKeys[i] = leaderboardId[i] + deviceId;

			// Create the leaderboard rank key in the dictionary if it doesn't exist
			selfRef.SetupRanksKey(leaderboardKeys[i]);

			// Mark the rank as active (being processed)
			rankStorage[leaderboardKeys[i]].isActive = true;

			if(!rankStorage[leaderboardKeys[i]].isReady || forceRefresh){
				pendingQueryLeaderboardIds += (!string.IsNullOrEmpty(pendingQueryLeaderboardIds) ? "," : "") + leaderboardId[i];
				pendingQueryScores += (!string.IsNullOrEmpty(pendingQueryScores) ? "," : "") + score[i];
				pendingQueryTimePeriods += (!string.IsNullOrEmpty(pendingQueryTimePeriods) ? "," : "") + timePeriod[i];
			}
		}

		if(!string.IsNullOrEmpty(pendingQueryLeaderboardIds)){
			Dictionary<string, string> postData = new Dictionary<string, string>();
			postData.Add("action", "get_leaderboard_rank_combined");
			postData.Add("leaderboard", pendingQueryLeaderboardIds);
			postData.Add("score", pendingQueryScores);
			postData.Add("time", pendingQueryTimePeriods);

			if(deviceId != string.Empty)
				postData.Add("device", deviceId);

			UnityWebRequest leaderboardRankRequest = DoWebRequest(postData, true, true);
			DownloadHandler leaderboardRankDownloadHandler = leaderboardRankRequest.downloadHandler;

			// Wait for the web request to complete
			yield return leaderboardRankRequest.SendWebRequest();

			if(leaderboardRankRequest.isHttpError || leaderboardRankRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRankRequest.error)){
				GoogleAnalytics.Instance.LogError("Failed to get leaderboard rank data! " + leaderboardRankRequest.error);

				foreach(string rankKey in leaderboardKeys)
				{
					rankStorage[rankKey].isError = true;
					rankStorage[rankKey].isActive = false;
				}

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Rank failed for " + leaderboardRankKey + " error: " + leaderboardRankRequest.error);

				if(OnRankRequestFailed != null)
					OnRankRequestFailed.Invoke(leaderboardRankKey, leaderboardRankRequest.error);

				yield break;
			}

			string errorResponse;

			if(IsErrorResponse(leaderboardRankKey, leaderboardRankDownloadHandler.text, out errorResponse)){
				GoogleAnalytics.Instance.LogError("Failed to get leaderboard rank data! " + errorResponse);

				foreach(string rankKey in leaderboardKeys)
				{
					rankStorage[rankKey].isError = true;
					rankStorage[rankKey].isActive = false;
				}

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Rank failed for " + leaderboardRankKey + " error: " + errorResponse);

				if(OnRankRequestFailed != null)
					OnRankRequestFailed.Invoke(leaderboardRankKey, errorResponse);

				yield break;
			}

			try {
				RawCombinedRankResponse combinedLeaderboardResponse = JsonUtility.FromJson<RawCombinedRankResponse>(leaderboardRankDownloadHandler.text);

				for(int i=0;i < combinedLeaderboardResponse.response.Length;i++)
				{
					string rankKey = combinedLeaderboardResponse.response[i].leaderboardId + deviceId;

					rankStorage[rankKey].response = combinedLeaderboardResponse.response[i].rankData;
					rankStorage[rankKey].isReady = true;
				}
			} catch(System.Exception e){
				GoogleAnalytics.Instance.LogError("Rank JSON data invalid!" + e.Message, false);

				foreach(string rankKey in leaderboardKeys)
				{
					rankStorage[rankKey].isError = true;
					rankStorage[rankKey].isActive = false;
				}

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Rank failed for " + leaderboardRankKey + " error: " + e.Message);

				if(OnRankRequestFailed != null)
					OnRankRequestFailed.Invoke(leaderboardRankKey, e.Message);

				yield break;
			}

			// Cleanup the WWW request data
			leaderboardRankRequest.Dispose();
			leaderboardRankDownloadHandler.Dispose();

			foreach(string rankKey in leaderboardKeys)
			{
				rankStorage[rankKey].isReady = true;
				rankStorage[rankKey].isActive = false;
			}

			if(debugMode == DebugModeTypes.Full){
				foreach(string rankKey in leaderboardKeys)
				{
					Debug.Log("[DEBUG] Rank ready for " + rankKey + " rank is " + rankStorage[rankKey].response);
				}
			}
		} else {
			cachedRequests++;

			if(debugMode == DebugModeTypes.Full){
				foreach(string rankKey in leaderboardKeys)
				{
					Debug.Log("[DEBUG] Rank for " + rankKey + " loaded from cache as " + rankStorage[rankKey].response);
				}
			}
		}

		foreach(string rankKey in leaderboardKeys)
		{
			// Trigger the OnLeaderboardRankReady action
			if(OnRankDone != null)
				OnRankDone.Invoke(rankKey, rankStorage[rankKey]);
		}
	}

	private IEnumerator DoGetLeaderboardRankData(string leaderboardId, int score, TimePeriod timePeriod = TimePeriod.AllTime, string deviceId = "", bool forceRefresh = false)
	{
		string leaderboardRankStorageRefId = leaderboardId + timePeriod + deviceId;

		// Only re-download the leaderboard if it's not already ready and this isn't a force refresh request (otherwise we'll use the cached version)
		if(!rankStorage[leaderboardRankStorageRefId].isReady || forceRefresh){
			// Immediately check if we have an internet connection and exit early if not
			if(Application.internetReachability == NetworkReachability.NotReachable){
				if(selfRef.debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Failed to get rank for " + leaderboardId + "! No internet connection");

				if(OnRankConnectionFailed != null)
					OnRankConnectionFailed.Invoke(leaderboardId);

				yield break;
			}

			// Mark the rank as active (being processed)
			rankStorage[leaderboardRankStorageRefId].isActive = true;
			
			Dictionary<string, string> postData = new Dictionary<string, string>();
			postData.Add("action", "get_leaderboard_rank");
			postData.Add("leaderboard", leaderboardId);
			postData.Add("score", score.ToString());
			postData.Add("time", timePeriod.ToString());

			if(deviceId != string.Empty)
				postData.Add("device", deviceId);

			UnityWebRequest leaderboardRankRequest = DoWebRequest(postData, true, true);
			DownloadHandler leaderboardRankDownloadHandler = leaderboardRankRequest.downloadHandler;

			// Wait for the web request to complete
			yield return leaderboardRankRequest.SendWebRequest();

			if(leaderboardRankRequest.isHttpError || leaderboardRankRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRankRequest.error)){
				GoogleAnalytics.Instance.LogError("Failed to get leaderboard rank data! " + leaderboardRankRequest.error);

				rankStorage[leaderboardRankStorageRefId].isError = true;
				rankStorage[leaderboardRankStorageRefId].isActive = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Rank failed for " + leaderboardId + " error: " + leaderboardRankRequest.error);

				if(OnRankRequestFailed != null)
					OnRankRequestFailed.Invoke(leaderboardId, leaderboardRankRequest.error);

				yield break;
			}

			string errorResponse;

			if(IsErrorResponse(leaderboardId, leaderboardRankDownloadHandler.text, out errorResponse)){
				GoogleAnalytics.Instance.LogError("Failed to get leaderboard rank data! " + errorResponse);

				rankStorage[leaderboardRankStorageRefId].isError = true;
				rankStorage[leaderboardRankStorageRefId].isActive = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Rank failed for " + leaderboardId + " error: " + errorResponse);

				if(OnRankRequestFailed != null)
					OnRankRequestFailed.Invoke(leaderboardId, errorResponse);

				yield break;
			}

			try {
				#if UNITY_5 || UNITY_2017_1_OR_NEWER
					rankStorage[leaderboardRankStorageRefId] = JsonUtility.FromJson<RankResponse>(leaderboardRankDownloadHandler.text);
				#else
					JSONNode jsonData = JSON.Parse(leaderboardRankDownloadHandler.text);
					rankStorage[leaderboardRankStorageRefId].response = jsonData["response"].ToString();
				#endif

				rankStorage[leaderboardRankStorageRefId].isReady = true;
			} catch(System.Exception e){
				GoogleAnalytics.Instance.LogError("Rank JSON data invalid!" + e.Message, false);

				rankStorage[leaderboardRankStorageRefId].isError = true;
				rankStorage[leaderboardRankStorageRefId].isActive = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Rank failed for " + leaderboardId + " error: " + e.Message);

				if(OnRankRequestFailed != null)
					OnRankRequestFailed.Invoke(leaderboardId, e.Message);

				yield break;
			}

			// Cleanup the WWW request data
			leaderboardRankRequest.Dispose();

			rankStorage[leaderboardRankStorageRefId].isReady = true;
			rankStorage[leaderboardRankStorageRefId].isActive = false;

			if(debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Rank ready for " + leaderboardId + " rank is " + rankStorage[leaderboardRankStorageRefId].response);
		} else {
			cachedRequests++;

			if(debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Rank for " + leaderboardId + " loaded from cache as " + rankStorage[leaderboardRankStorageRefId].response);
		}

		// Trigger the OnLeaderboardRankReady action
		if(OnRankDone != null)
			OnRankDone.Invoke(leaderboardId, rankStorage[leaderboardRankStorageRefId]);
	}

	private IEnumerator DoGetCombinedLeaderboardData(string leaderboardKey, List<string> leaderboardId, List<TimePeriod> timePeriod, List<int> pageNum, string deviceId = "", bool forceRefresh = false)
	{
		string[] leaderboardKeys = new string[leaderboardId.Count];

		string pendingQueryLeaderboardIds = "";
		string pendingQueryTimePeriods = "";
		string pendingQueryPageNums = "";

		for(int i=0;i < leaderboardId.Count;i++)
		{
			leaderboardKeys[i] = leaderboardId[i] + deviceId + "_p" + pageNum[i];

			// Create the leaderboard key in the dictionary if it doesn't exist
			selfRef.SetupLeaderboardsKey(leaderboardKeys[i]);

			if(!leaderboardStorage[leaderboardKeys[i]].isReady || forceRefresh){
				pendingQueryLeaderboardIds += (!string.IsNullOrEmpty(pendingQueryLeaderboardIds) ? "," : "") + leaderboardId[i];
				pendingQueryTimePeriods += (!string.IsNullOrEmpty(pendingQueryTimePeriods) ? "," : "") + timePeriod[i];
				pendingQueryPageNums += (!string.IsNullOrEmpty(pendingQueryPageNums) ? "," : "") + pageNum[i];
			}
		}

		if(!string.IsNullOrEmpty(pendingQueryLeaderboardIds)){
			Dictionary<string, string> postData = new Dictionary<string, string>();
			postData.Add("action", "get_leaderboard_combined");
			postData.Add("leaderboard", pendingQueryLeaderboardIds);
			postData.Add("time", pendingQueryTimePeriods);
			postData.Add("page", pendingQueryPageNums);
			postData.Add("perpage", resultsPerPage.ToString());

			if(deviceId != string.Empty)
				postData.Add("device", deviceId);

			UnityWebRequest leaderboardRequest = DoWebRequest(postData, true, true);
			DownloadHandler leaderboardDownloadHandler = leaderboardRequest.downloadHandler;

			// Wait for the web request to complete
			yield return leaderboardRequest.SendWebRequest();

			if(leaderboardRequest.isHttpError || leaderboardRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRequest.error)){
				GoogleAnalytics.Instance.LogError("Failed to get leaderboard data! " + leaderboardRequest.error);

				foreach(string key in leaderboardKeys)
				{
					leaderboardStorage[key].isError = true;
					leaderboardStorage[key].isActive = false;
				}

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Leaderboard failed for " + leaderboardId + " error: " + leaderboardRequest.error);

				if(OnLeaderboardRequestFailed != null)
					OnLeaderboardRequestFailed.Invoke(leaderboardKey, leaderboardRequest.error);

				yield break;
			}

			string errorResponse;

			if(IsErrorResponse(leaderboardKey, leaderboardDownloadHandler.text, out errorResponse)){
				GoogleAnalytics.Instance.LogError("Failed to get leaderboard data! " + errorResponse);

				foreach(string key in leaderboardKeys)
				{
					leaderboardStorage[key].isError = true;
					leaderboardStorage[key].isActive = false;
				}

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Data failed for " + leaderboardKey + " error " + errorResponse);

				if(OnLeaderboardRequestFailed != null)
					OnLeaderboardRequestFailed.Invoke(leaderboardKey, errorResponse);

				yield break;
			}

			try {
				RawCombinedLeaderboardResponse combinedLeaderboardResponse = JsonUtility.FromJson<RawCombinedLeaderboardResponse>(leaderboardDownloadHandler.text);

				for(int i=0;i < combinedLeaderboardResponse.response.Length;i++)
				{
					string key = combinedLeaderboardResponse.response[i].leaderboardId + deviceId + "_p" + combinedLeaderboardResponse.response[i].pageNum;

					leaderboardStorage[key].response = combinedLeaderboardResponse.response[i].data;
					leaderboardStorage[key].isReady = true;
				}
			} catch(System.Exception e){
				GoogleAnalytics.Instance.LogError("Leaderboard JSON data invalid! " + e.Message, false);

				foreach(string key in leaderboardKeys)
				{
					leaderboardStorage[key].isError = true;
					leaderboardStorage[key].isActive = false;
				}

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Data failed for " + leaderboardKey + " error: " + e.Message);

				if(OnLeaderboardRequestFailed != null)
					OnLeaderboardRequestFailed.Invoke(leaderboardKey, e.Message);

				yield break;
			}

			// Cleanup the request data
			leaderboardRequest.Dispose();
			leaderboardDownloadHandler.Dispose();

			foreach(string key in leaderboardKeys)
			{
				leaderboardStorage[key].isReady = true;
				leaderboardStorage[key].isActive = false;
			}

			if(debugMode == DebugModeTypes.Full){
				foreach(string key in leaderboardKeys)
				{
					Debug.Log("[DEBUG] Data ready for " + key + " is " + leaderboardStorage[key].response);
				}
			}
		} else {
			cachedRequests++;

			if(debugMode == DebugModeTypes.Full){
				foreach(string key in leaderboardKeys)
				{
					Debug.Log("[DEBUG] Data ready for " + key + " is " + leaderboardStorage[key].response);
				}
			}
		}

		foreach(string key in leaderboardKeys)
		{
			// Trigger the OnLeaderboardDone action
			if(OnLeaderboardDone != null)
				OnLeaderboardDone.Invoke(key, leaderboardStorage[key]);
		}
	}

	private IEnumerator DoGetLeaderboardData(string leaderboardId, string deviceId = "", TimePeriod timePeriod = TimePeriod.AllTime, int pageNum = 0, bool forceRefresh = false)
	{
		string leaderboardStorageRef = leaderboardId + deviceId + timePeriod + pageNum;

		// Only re-download the leaderboard if it's not already ready and this isn't a force refresh request (otherwise we'll use the cached version)
		if(!leaderboardStorage[leaderboardStorageRef].isReady || forceRefresh){
			// Immediately check if we have an internet connection and exit early if not
			if(Application.internetReachability == NetworkReachability.NotReachable){
				if(selfRef.debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Failed to get leaderboard for " + leaderboardId + "! No internet connection");

				if(OnLeaderboardConnectionFailed != null)
					OnLeaderboardConnectionFailed.Invoke(leaderboardId);

				yield break;
			}

			// Mark the rank as active (being processed)
			leaderboardStorage[leaderboardStorageRef].isActive = true;

			Dictionary<string, string> postData = new Dictionary<string, string>();
			postData.Add("action", "get_leaderboard");
			postData.Add("leaderboard", leaderboardId);
			postData.Add("time", timePeriod.ToString());
			postData.Add("page", pageNum.ToString());
			postData.Add("perpage", resultsPerPage.ToString());

			if(deviceId != string.Empty)
				postData.Add("device", deviceId);

			UnityWebRequest leaderboardRequest = DoWebRequest(postData, true, true);
			DownloadHandler leaderboardDownloadHandler = leaderboardRequest.downloadHandler;

			// Wait for the web request to complete
			yield return leaderboardRequest.SendWebRequest();

			if(leaderboardRequest.isHttpError || leaderboardRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRequest.error)){
				GoogleAnalytics.Instance.LogError("Failed to get leaderboard data! " + leaderboardRequest.error);

				leaderboardStorage[leaderboardStorageRef].isError = true;
				leaderboardStorage[leaderboardStorageRef].isActive = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Leaderboard failed for " + leaderboardId + " error: " + leaderboardRequest.error);

				if(OnLeaderboardRequestFailed != null)
					OnLeaderboardRequestFailed.Invoke(leaderboardId, leaderboardRequest.error);

				yield break;
			}

			string errorResponse;

			if(IsErrorResponse(leaderboardId, leaderboardDownloadHandler.text, out errorResponse)){
				GoogleAnalytics.Instance.LogError("Failed to get leaderboard data! " + errorResponse);

				leaderboardStorage[leaderboardStorageRef].isError = true;
				leaderboardStorage[leaderboardStorageRef].isActive = false;

				if(errorResponse == "ERROR_NO_RESULTS"){
					// Cache the empty leaderboard when there's no results
					leaderboardStorage[leaderboardStorageRef].response = new List<LeaderboardStorage>();
					leaderboardStorage[leaderboardStorageRef].isReady = true;
				}

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Leaderboard failed for " + leaderboardId + " error: " + errorResponse);

				if(OnLeaderboardRequestFailed != null)
					OnLeaderboardRequestFailed.Invoke(leaderboardId, errorResponse);

				yield break;
			}

			try {
				#if UNITY_5_3_OR_NEWER
					leaderboardStorage[leaderboardStorageRef] = JsonUtility.FromJson<LeaderboardResponse>(leaderboardDownloadHandler.text);

					// Was trying with this to see if it would help.. but it didn't
					//leaderboardStorage[leaderboardStorageRef] = new LeaderboardResponse();
					//leaderboardStorage[leaderboardStorageRef].response = new List<LeaderboardStorage>();
					//leaderboardStorage[leaderboardStorageRef].response.Add(new LeaderboardStorage());
					//leaderboardStorage[leaderboardStorageRef].response[0].nickname = "Test Successful";
				#else
					leaderboardStorage[leaderboardStorageRef].response = new List<LeaderboardStorage>();
					JSONNode jsonData = JSON.Parse(leaderboardDownloadHandler.text);

					// Iterate through each row of the leaderboard adding the results to the class storage
					for(int rowId=0;rowId < jsonData["response"].AsArray.Count;rowId++)
					{
						JSONNode row = jsonData["response"].AsArray[rowId];
						LeaderboardStorage leaderboardRowData = new LeaderboardStorage();

						leaderboardRowData.device_identifier = row["device_identifier"];
						leaderboardRowData.nickname = row["nickname"];
						leaderboardRowData.score = int.Parse(row["score"]);
						leaderboardRowData.timestamp = long.Parse(row["timestamp"]);

						leaderboardStorage[leaderboardStorageRef].response.Add(leaderboardRowData);
					}
				#endif

				leaderboardStorage[leaderboardStorageRef].isReady = true;
			} catch(System.Exception e){
				GoogleAnalytics.Instance.LogError("Leaderboard JSON data invalid!" + e.Message, false);

				leaderboardStorage[leaderboardStorageRef].isError = true;
				leaderboardStorage[leaderboardStorageRef].isActive = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Leaderboard failed for " + leaderboardId + " error: " + e.Message);

				if(OnLeaderboardRequestFailed != null)
					OnLeaderboardRequestFailed.Invoke(leaderboardId, e.Message);
				yield break;
			}

			// Cleanup the WWW request data
			leaderboardRequest.Dispose();

			leaderboardStorage[leaderboardStorageRef].isReady = true;
			leaderboardStorage[leaderboardStorageRef].isActive = false;

			if(debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Leaderboard ready for " + leaderboardId + " found " + leaderboardStorage[leaderboardStorageRef].Count() + " rows");
		} else {
			cachedRequests++;

			if(leaderboardStorage[leaderboardStorageRef].Count() <= 0){
				if(OnLeaderboardRequestFailed != null)
					OnLeaderboardRequestFailed.Invoke(leaderboardId, "ERROR_NO_RESULTS");

				yield break;
			}

			if(debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Leaderboard for " + leaderboardId + " loaded from cache with " + leaderboardStorage[leaderboardStorageRef].Count() + " rows");
		}

		// Trigger the OnLeaderboardReady action
		if(OnLeaderboardDone != null)
			OnLeaderboardDone.Invoke(leaderboardId, leaderboardStorage[leaderboardStorageRef]);
	}

	private IEnumerator DoAdjustLeaderboardData(string leaderboardId, string deviceId, string nickname, int scoreAdjust)
	{
		// Mark the leaderboard submission as true (active submission)
		leaderboardSubmissions[leaderboardId] = true;

		SubmissionCache cachedSubmission = leaderboardSubmissionCache.ContainsKey(leaderboardId + deviceId) ? leaderboardSubmissionCache[leaderboardId + deviceId] : null;

		// Don't bother running the query unless either it has never been ran before, the nickname has changed or the scoreAdjustment is not 0
		if(cachedSubmission == null || cachedSubmission.nickname != nickname || scoreAdjust != 0){
			if(cachedSubmission == null){
				leaderboardSubmissionCache.Add(leaderboardId + deviceId, new SubmissionCache(nickname, 0)); // As we're in the score adjustment function we can't cache a score yet
			} else {
				cachedSubmission.nickname = nickname;
			}

			Dictionary<string, string> postData = new Dictionary<string, string>();
			postData.Add("action", "adjust_leaderboard");
			postData.Add("leaderboard", leaderboardId);
			postData.Add("nickname", WWW.EscapeURL(nickname, Encoding.UTF8));
			postData.Add("score", scoreAdjust.ToString());
			postData.Add("perpage", resultsPerPage.ToString());

			if(deviceId != string.Empty)
				postData.Add("device", deviceId);

			UnityWebRequest leaderboardRequest = DoWebRequest(postData, true, true);
			DownloadHandler leaderboardDownloadHandler = leaderboardRequest.downloadHandler;

			// Wait for the web request to complete
			yield return leaderboardRequest.SendWebRequest();

			if(leaderboardRequest.isHttpError || leaderboardRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRequest.error)){
				GoogleAnalytics.Instance.LogError("Failed to adjust leaderboard! " + leaderboardRequest.error);

				leaderboardSubmissions[leaderboardId] = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Adjust failed for " + leaderboardId + " error: " + leaderboardRequest.error);

				if(OnSubmitRequestFailed != null)
					OnSubmitRequestFailed.Invoke(leaderboardId, leaderboardRequest.error);

				yield break;
			}

			string errorResponse;

			if(IsErrorResponse(leaderboardId, leaderboardDownloadHandler.text, out errorResponse)){
				GoogleAnalytics.Instance.LogError("Failed to adjust leaderboard! " + errorResponse);

				leaderboardSubmissions[leaderboardId] = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Adjust failed for " + leaderboardId + " error: " + errorResponse);

				if(OnSubmitRequestFailed != null)
					OnSubmitRequestFailed.Invoke(leaderboardId, errorResponse);

				yield break;
			}

			// Cleanup the WWW request data
			leaderboardRequest.Dispose();
		} else {
			cachedRequests++;
		}

		leaderboardSubmissions[leaderboardId] = false;

		if(debugMode == DebugModeTypes.Full)
			Debug.Log("[DEBUG] Adjust complete for " + leaderboardId + " with score " + scoreAdjust);

		// Trigger the OnLeaderboardSubmitComplete action
		if(OnSubmitDone != null)
			OnSubmitDone.Invoke(leaderboardId);
	}

	private IEnumerator DoDeleteLeaderboardData(string leaderboardId, string deviceId)
	{
		// Mark the leaderboard submission as true (active submission)
		leaderboardSubmissions[leaderboardId] = true;

		Dictionary<string, string> postData = new Dictionary<string, string>();
		postData.Add("action", "delete_leaderboard");
		postData.Add("leaderboard", leaderboardId);
		postData.Add("perpage", resultsPerPage.ToString());

		if(deviceId != string.Empty)
			postData.Add("device", deviceId);

		UnityWebRequest leaderboardRequest = DoWebRequest(postData, true, true);
		DownloadHandler leaderboardDownloadHandler = leaderboardRequest.downloadHandler;

		// Wait for the web request to complete
		yield return leaderboardRequest.SendWebRequest();

		if(leaderboardRequest.isHttpError || leaderboardRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRequest.error)){
			GoogleAnalytics.Instance.LogError("Failed to delete leaderboard! " + leaderboardRequest.error);

			leaderboardSubmissions[leaderboardId] = false;

			if(debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Delete failed for " + leaderboardId + " error: " + leaderboardRequest.error);

			if(OnSubmitRequestFailed != null)
				OnSubmitRequestFailed.Invoke(leaderboardId, leaderboardRequest.error);

			yield break;
		}

		string errorResponse;

		if(IsErrorResponse(leaderboardId, leaderboardDownloadHandler.text, out errorResponse)){
			GoogleAnalytics.Instance.LogError("Failed to delete leaderboard! " + errorResponse);

			leaderboardSubmissions[leaderboardId] = false;

			if(debugMode == DebugModeTypes.Full)
				Debug.Log("[DEBUG] Delete failed for " + leaderboardId + " error: " + errorResponse);

			if(OnSubmitRequestFailed != null)
				OnSubmitRequestFailed.Invoke(leaderboardId, errorResponse);

			yield break;
		}

		// Cleanup the WWW request data
		leaderboardRequest.Dispose();

		leaderboardSubmissions[leaderboardId] = false;

		if(debugMode == DebugModeTypes.Full)
			Debug.Log("[DEBUG] Delete complete for " + leaderboardId);

		// Trigger the OnLeaderboardSubmitComplete action
		if(OnSubmitDone != null)
			OnSubmitDone.Invoke(leaderboardId);
	}

	private IEnumerator DoAdjustCombinedLeaderboardData(string leaderboardSubmissionKey, List<string> leaderboardId, List<int> scoreChange, string deviceId, string nickname)
	{
		// Mark the leaderboard submission as true (active submission)
		leaderboardSubmissions[leaderboardSubmissionKey] = true;

		string pendingQueryLeaderboardIds = "";
		string pendingQueryScores = "";

		for(int i=0;i < leaderboardId.Count;i++)
		{
			SubmissionCache cachedSubmission = leaderboardSubmissionCache.ContainsKey(leaderboardId[i] + deviceId) ? leaderboardSubmissionCache[leaderboardId[i] + deviceId] : null;

			if(cachedSubmission == null || cachedSubmission.nickname != nickname || scoreChange[i] != 0){
				if(cachedSubmission == null){
					leaderboardSubmissionCache.Add(leaderboardId[i] + deviceId, new SubmissionCache(nickname, 0)); // As we're in the score adjustment function we can't cache a score yet
				} else {
					cachedSubmission.nickname = nickname;
				}

				pendingQueryLeaderboardIds += (!string.IsNullOrEmpty(pendingQueryLeaderboardIds) ? "," : "") + leaderboardId[i];
				pendingQueryScores += (!string.IsNullOrEmpty(pendingQueryScores) ? "," : "") + scoreChange[i];
			}
		}

		if(!string.IsNullOrEmpty(pendingQueryLeaderboardIds)){
			Dictionary<string, string> postData = new Dictionary<string, string>();
			postData.Add("action", "adjust_leaderboard_combined");
			postData.Add("leaderboard", pendingQueryLeaderboardIds);
			postData.Add("nickname", WWW.EscapeURL(nickname, Encoding.UTF8));
			postData.Add("score", pendingQueryScores);
			postData.Add("perpage", resultsPerPage.ToString());

			if(deviceId != string.Empty)
				postData.Add("device", deviceId);

			UnityWebRequest leaderboardRequest = DoWebRequest(postData, true, true);
			DownloadHandler leaderboardDownloadHandler = leaderboardRequest.downloadHandler;

			// Wait for the web request to complete
			yield return leaderboardRequest.SendWebRequest();

			if(leaderboardRequest.isHttpError || leaderboardRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRequest.error)){
				GoogleAnalytics.Instance.LogError("Failed to adjust combined leaderboard! " + leaderboardRequest.error);

				leaderboardSubmissions[leaderboardSubmissionKey] = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Adjust combined failed for " + leaderboardSubmissionKey + " error: " + leaderboardRequest.error);

				if(OnSubmitRequestFailed != null)
					OnSubmitRequestFailed.Invoke(leaderboardSubmissionKey, leaderboardRequest.error);

				yield break;
			}

			string errorResponse;

			if(IsErrorResponse(leaderboardSubmissionKey, leaderboardDownloadHandler.text, out errorResponse)){
				GoogleAnalytics.Instance.LogError("Failed to adjust combined leaderboard! " + errorResponse);

				leaderboardSubmissions[leaderboardSubmissionKey] = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Adjust combined failed for " + leaderboardSubmissionKey + " error: " + errorResponse);

				if(OnSubmitRequestFailed != null)
					OnSubmitRequestFailed.Invoke(leaderboardSubmissionKey, errorResponse);

				yield break;
			}

			// Cleanup the WWW request data
			leaderboardRequest.Dispose();
		} else {
			cachedRequests++;
		}

		leaderboardSubmissions[leaderboardSubmissionKey] = false;

		if(debugMode == DebugModeTypes.Full)
			Debug.Log("[DEBUG] Adjust combined complete for " + leaderboardSubmissionKey);

		// Trigger the OnLeaderboardSubmitComplete action
		if(OnSubmitDone != null)
			OnSubmitDone.Invoke(leaderboardSubmissionKey);
	}

	private IEnumerator DoSetCombinedLeaderboardData(string leaderboardSubmissionKey, List<string> leaderboardId, List<int> score, string deviceId, string nickname)
	{
		// Mark the leaderboard submission as true (active submission)
		leaderboardSubmissions[leaderboardSubmissionKey] = true;

		string pendingQueryLeaderboardIds = "";
		string pendingQueryScores = "";

		for(int i=0;i < leaderboardId.Count;i++)
		{
			SubmissionCache cachedSubmission = leaderboardSubmissionCache.ContainsKey(leaderboardId[i] + deviceId) ? leaderboardSubmissionCache[leaderboardId[i] + deviceId] : null;

			// Don't bother running the query unless either has never been ran before, the nickname has changed or the score is not the same as the previous score
			if(cachedSubmission == null || cachedSubmission.nickname != nickname || cachedSubmission.score != score[i]){
				if(cachedSubmission == null){
					leaderboardSubmissionCache.Add(leaderboardId[i] + deviceId, new SubmissionCache(nickname, score[i]));
				} else {
					cachedSubmission.nickname = nickname;
					cachedSubmission.score = score[i];
				}

				pendingQueryLeaderboardIds += (!string.IsNullOrEmpty(pendingQueryLeaderboardIds) ? "," : "") + leaderboardId[i];
				pendingQueryScores += (!string.IsNullOrEmpty(pendingQueryScores) ? "," : "") + score[i];
			}
		}

		if(!string.IsNullOrEmpty(pendingQueryLeaderboardIds)){
			Dictionary<string, string> postData = new Dictionary<string, string>();
			postData.Add("action", "set_leaderboard_combined");
			postData.Add("leaderboard", pendingQueryLeaderboardIds);
			postData.Add("nickname", WWW.EscapeURL(nickname, Encoding.UTF8));
			postData.Add("score", pendingQueryScores);
			postData.Add("perpage", resultsPerPage.ToString());

			if(deviceId != string.Empty)
				postData.Add("device", deviceId);

			UnityWebRequest leaderboardRequest = DoWebRequest(postData, true, true);
			DownloadHandler leaderboardDownloadHandler = leaderboardRequest.downloadHandler;

			// Wait for the web request to complete
			yield return leaderboardRequest.SendWebRequest();

			if(leaderboardRequest.isHttpError || leaderboardRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRequest.error)){
				GoogleAnalytics.Instance.LogError("Failed to submit combined leaderboard! " + leaderboardRequest.error);

				leaderboardSubmissions[leaderboardSubmissionKey] = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Submit combined failed for " + leaderboardSubmissionKey + " error: " + leaderboardRequest.error);

				if(OnSubmitRequestFailed != null)
					OnSubmitRequestFailed.Invoke(leaderboardSubmissionKey, leaderboardRequest.error);

				yield break;
			}

			string errorResponse;

			if(IsErrorResponse(leaderboardSubmissionKey, leaderboardDownloadHandler.text, out errorResponse)){
				GoogleAnalytics.Instance.LogError("Failed to submit combined leaderboard! " + errorResponse);

				leaderboardSubmissions[leaderboardSubmissionKey] = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Submit combined failed for " + leaderboardSubmissionKey + " error: " + errorResponse);

				if(OnSubmitRequestFailed != null)
					OnSubmitRequestFailed.Invoke(leaderboardSubmissionKey, errorResponse);

				yield break;
			}

			// Cleanup the WWW request data
			leaderboardRequest.Dispose();
		} else {
			cachedRequests++;
		}

		leaderboardSubmissions[leaderboardSubmissionKey] = false;

		if(debugMode == DebugModeTypes.Full)
			Debug.Log("[DEBUG] Submit combined complete for " + leaderboardSubmissionKey + " with score " + score);

		// Trigger the OnLeaderboardSubmitComplete action
		if(OnSubmitDone != null)
			OnSubmitDone.Invoke(leaderboardSubmissionKey);
	}

	// Setting the leaderboard doesn't touch the Leaderboards[..] data just incase we're reading at the same time as submitting
	private IEnumerator DoSetLeaderboardData(string leaderboardId, string deviceId, string nickname, int score)
	{
		// Mark the leaderboard submission as true (active submission)
		leaderboardSubmissions[leaderboardId] = true;

		SubmissionCache cachedSubmission = leaderboardSubmissionCache.ContainsKey(leaderboardId + deviceId) ? leaderboardSubmissionCache[leaderboardId + deviceId] : null;

		// Don't bother running the query unless either it has never been ran before, the nickname has changed or the score is not the same as the previous score
		if(cachedSubmission == null || cachedSubmission.nickname != nickname || cachedSubmission.score != score){
			if(cachedSubmission == null){
				leaderboardSubmissionCache.Add(leaderboardId + deviceId, new SubmissionCache(nickname, score));
			} else {
				cachedSubmission.nickname = nickname;
				cachedSubmission.score = score;
			}

			Dictionary<string, string> postData = new Dictionary<string, string>();
			postData.Add("action", "set_leaderboard");
			postData.Add("leaderboard", leaderboardId);
			postData.Add("nickname", WWW.EscapeURL(nickname, Encoding.UTF8));
			postData.Add("score", score.ToString());
			postData.Add("perpage", resultsPerPage.ToString());

			if(deviceId != string.Empty)
				postData.Add("device", deviceId);

			UnityWebRequest leaderboardRequest = DoWebRequest(postData, true, true);
			DownloadHandler leaderboardDownloadHandler = leaderboardRequest.downloadHandler;

			// Wait for the web request to complete
			yield return leaderboardRequest.SendWebRequest();

			if(leaderboardRequest.isHttpError || leaderboardRequest.isNetworkError || !string.IsNullOrEmpty(leaderboardRequest.error)){
				GoogleAnalytics.Instance.LogError("Failed to submit leaderboard! " + leaderboardRequest.error);

				leaderboardSubmissions[leaderboardId] = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Submit failed for " + leaderboardId + " error: " + leaderboardRequest.error);

				if(OnSubmitRequestFailed != null)
					OnSubmitRequestFailed.Invoke(leaderboardId, leaderboardRequest.error);

				yield break;
			}

			string errorResponse;

			if(IsErrorResponse(leaderboardId, leaderboardDownloadHandler.text, out errorResponse)){
				GoogleAnalytics.Instance.LogError("Failed to submit leaderboard! " + errorResponse);

				leaderboardSubmissions[leaderboardId] = false;

				if(debugMode == DebugModeTypes.Full)
					Debug.Log("[DEBUG] Submit failed for " + leaderboardId + " error: " + errorResponse);

				if(OnSubmitRequestFailed != null)
					OnSubmitRequestFailed.Invoke(leaderboardId, errorResponse);

				yield break;
			}

			// Cleanup the WWW request data
			leaderboardRequest.Dispose();
		} else {
			cachedRequests++;
		}

		leaderboardSubmissions[leaderboardId] = false;

		if(debugMode == DebugModeTypes.Full)
			Debug.Log("[DEBUG] Submit complete for " + leaderboardId + " with score " + score);

		// Trigger the OnLeaderboardSubmitComplete action
		if(OnSubmitDone != null)
			OnSubmitDone.Invoke(leaderboardId);
	}

	private bool IsErrorResponse(string leaderboardId, string response, out string responseError)
	{
		response = response.Replace("{\"response\":\"", "");
		response = response.Replace("\"}", "");

		bool isError = false;

		switch(response)
		{
			case "ERROR_INVALID_REQUEST":
			case "ERROR_MISSING_PLATFORM":
			case "ERROR_MISSING_PACKAGENAME":
			case "ERROR_MISSING_LEADERBOARDIDENTIFIER":
			case "ERROR_MISSING_DEVICEIDENTIFIER":
			case "ERROR_MISSING_SCORE":
			case "ERROR_MISSING_VALIDATIONTOKEN":
			case "ERROR_INVALID_CHECKSUM":
			case "ERROR_NOT_INSERTED":
			case "ERROR_DUPLICATE_VALIDATION_TOKEN":
				isError = true;
				break;

			case "ERROR_NO_RESULTS":
				isError = treatNoResultsAsError;
				break;
		}

		if(isError){
			GoogleAnalytics.Instance.LogError("Leaderboard " + response + " in " + leaderboardId);
			responseError = response;
		} else {
			responseError = string.Empty;
		}

		return isError;
	}

	private string GetSecurityToken()
	{
		// Generates a random token and XOR's it with our seed (must match the seed used on the serverside)
		return XORString(GenerateRandomToken(16), "!5i8!Rj0ls");
	}

	private string GenerateRandomToken(int Length = 16)
	{
		#if UNITY_5_4_OR_NEWER
			UnityEngine.Random.InitState((int)DateTime.Now.Ticks);
		#else
			UnityEngine.Random.seed = (int)DateTime.Now.Ticks;
		#endif

		// Just keeping the tokens simple because I'm scared of encoding issues
		const string Glpyhs = "abcdefghijklmnopqrstuvwxyz0123456789";
		StringBuilder Token = new StringBuilder(Length);

		for(int i=0;i < Length;i++)
			Token.Insert(i, Glpyhs[UnityEngine.Random.Range(0, Glpyhs.Length-1)]);

		return Token.ToString();
	}

	public List<string> namePart1 = new List<string>();
	public List<string> namePart2 = new List<string>();
	public List<string> namePart3 = new List<string>();

	public string GenerateRandomName()
	{
		#if UNITY_5_4_OR_NEWER
			UnityEngine.Random.InitState((int)DateTime.Now.Ticks);
		#else
			UnityEngine.Random.seed = (int)DateTime.Now.Ticks;
		#endif

		string outputName = namePart1[UnityEngine.Random.Range(0, namePart1.Count)];
		outputName += " " + namePart2[UnityEngine.Random.Range(0, namePart2.Count)];
		outputName += " " + namePart3[UnityEngine.Random.Range(0, namePart3.Count)];

		return outputName;
	}

	private string GenerateChecksum(string input)
	{
		// XOR the query string the generate a checksum to validate the request hasn't been changed on the transit from game to server
		return XORString(input, "!5i8!Rj0ls");
	}

	private string XORString(string Input, string Key = "")
	{
		if(!string.IsNullOrEmpty(Input)){
			int EncryptionJump = (Input.Length < 500 ? 1 : Mathf.FloorToInt(Input.Length / 500));
			int IterationLength = Mathf.CeilToInt(Input.Length / EncryptionJump);

			// To modify specific character of a string we need to use a stringbuilder type
			StringBuilder SaveDecryptedString = new StringBuilder(Input);

			for(int i=0;i < IterationLength;i++)
				SaveDecryptedString[i * EncryptionJump] = (char)(Input[i * EncryptionJump] ^ Key[(i * EncryptionJump) % Key.Length]);

			// Return the final string
			return SaveDecryptedString.ToString();
		}

		return String.Empty;
	}

}
