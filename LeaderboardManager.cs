using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Security.Cryptography;
using System.Text;

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
public class RankResponse {
	public string response;

	public bool isReady { get; set; }
	public bool isError { get; set; }
	public bool isActive { get; set; }
}

public class LeaderboardManager : MonoBehaviour {

	// Static script reference
	public static LeaderboardManager selfRef;

	// Leaderboard dictionary (the keys are the leaderboard identifiers)
	private Dictionary<string, LeaderboardResponse> leaderboardStorage = new Dictionary<string, LeaderboardResponse>();

	// Rank dictionary (the keys are the leaderboard identifiers)
	private Dictionary<string, RankResponse> rankStorage = new Dictionary<string, RankResponse>();

	// This is a fixed value because I have complicated serverside caching of queries which becomes messy if the results per page changes for each request
	public int resultsPerPage = 20;

	// When true extra messages will be logged which information about the requests
	public bool debugMode = false;

	// Whether having no results should be treated as an error
	// Can be useful if you want to show a message instead of an empty leaderboard
	public bool treatNoResultsAsError = false;

	// Only allow one score submission request at once
	private bool isScoreSubmitting = false;
	
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
	public static bool IsSubmitActive()
	{
		return selfRef.isScoreSubmitting;
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

	// Gets a leaderboard by the leaderboardId (returns null if there's no leaderboard data ready, or returns a blank LeaderboardResponse if the request hasn't finished yet)
	// Check with IsLeaderboardReady(..) first if you want to know the status, or wait for the callback
	public static LeaderboardResponse GetLeaderboard(string leaderboardId)
	{
		return selfRef.leaderboardStorage.ContainsKey(leaderboardId) ? selfRef.leaderboardStorage[leaderboardId] : null;
	}

	// Gets a leaderboard rank by the leaderboard identifier (returns null if there's no leaderboard rank ready, or returns a blank RankResponse if the request is hasn't finished yet)
	// Check with IsRankReady(..) first if you want to know the status, or wait for the callback
	public static RankResponse GetRank(string leaderboardId)
	{
		return selfRef.rankStorage.ContainsKey(leaderboardId) ? selfRef.rankStorage[leaderboardId] : null;
	}

	private void SetupRanksKey(string leaderboardId)
	{
		// Add a rank key if one doesn't exist in the dictionary
		if(!selfRef.rankStorage.ContainsKey(leaderboardId))
			selfRef.rankStorage.Add(leaderboardId, new RankResponse());

		// Reset the ready and error status
		selfRef.rankStorage[leaderboardId].isReady = false;
		selfRef.rankStorage[leaderboardId].isError = false;
		selfRef.rankStorage[leaderboardId].isActive = false;
	}

	private void SetupLeaderboardsKey(string leaderboardId)
	{
		// Add a leaderboard key if one doesn't exist in the dictionary
		if(!selfRef.leaderboardStorage.ContainsKey(leaderboardId))
			selfRef.leaderboardStorage.Add(leaderboardId, new LeaderboardResponse());

		// Reset the ready and error status
		selfRef.leaderboardStorage[leaderboardId].isReady = false;
		selfRef.leaderboardStorage[leaderboardId].isError = false;
		selfRef.leaderboardStorage[leaderboardId].isActive = false;
	}

	// Send a request for the leaderboard submissions within the requested leaderboard (only from this device if DeviceId is defined)
	public static void GetLeaderboardData(string leaderboardId, string deviceId = "", TimePeriod timePeriod = TimePeriod.AllTime, int pageNum = 0)
	{
		if(!IsLeaderboardActive(leaderboardId)){
			// Mark the leaderboard as not ready (it may still contain data but isReady is false when the last GetLeaderboardData request has not completed yet) (and may never will due to errors)
			selfRef.SetupLeaderboardsKey(leaderboardId);

			// Don't even bother starting the routine if we don't have a working internet connection
			if(Application.internetReachability != NetworkReachability.NotReachable){
				// Start the leaderboard routine, this will send a server request for the wanted data
				selfRef.StartCoroutine(selfRef.DoGetLeaderboardData(leaderboardId, deviceId, timePeriod, pageNum));
			} else {
				if(selfRef.debugMode)
					Debug.Log("[DEBUG] Failed to get leaderboard for " + leaderboardId + "! No internet connection");

				if(OnLeaderboardConnectionFailed != null)
					OnLeaderboardConnectionFailed.Invoke(leaderboardId);
			}
		} else {
			if(selfRef.debugMode)
				Debug.Log("[DEBUG] Get leaderboard for " + leaderboardId + " was already active");

			if(OnLeaderboardAlreadyPending != null)
				OnLeaderboardAlreadyPending.Invoke(leaderboardId);
		}
	}

	// Send a request for what rank a score would be in the leaderboard
	public static void GetLeaderboardRankData(string leaderboardId, int score, TimePeriod timePeriod = TimePeriod.AllTime, string deviceId = "")
	{
		if(!IsRankActive(leaderboardId)){
			// Mark the leaderboard as not ready (it may still contain data but isReady is false when the last GetLeaderboardData request has not completed yet) (and may never will due to errors)
			selfRef.SetupRanksKey(leaderboardId);

			// Don't even bother starting the routine if we don't have a working internet connection
			if(Application.internetReachability != NetworkReachability.NotReachable){
				// Start the leaderboard routine, this will send a server request for the rank a score has in the leaderboard
				selfRef.StartCoroutine(selfRef.DoGetLeaderboardRankData(leaderboardId, score, timePeriod, deviceId));
			} else {
				if(selfRef.debugMode)
					Debug.Log("[DEBUG] Failed to get rank for " + leaderboardId + "! No internet connection");

				if(OnRankConnectionFailed != null)
					OnRankConnectionFailed.Invoke(leaderboardId);
			}
		} else {
			if(selfRef.debugMode)
				Debug.Log("[DEBUG] Get rank for " + leaderboardId + " was already active");

			if(OnRankAlreadyPending != null)
				OnRankAlreadyPending.Invoke(leaderboardId);
		}
	}

	// Submit a score to the requested leaderboard
	public static void SetLeaderboardData(string leaderboardId, string deviceId, string nickname, int score)
	{
		if(!IsSubmitActive()){
			// Don't even bother starting the routine if we don't have a working internet connection
			if(Application.internetReachability != NetworkReachability.NotReachable){
				// Start the leaderboard routine, this will send a server request to submit the score
				selfRef.StartCoroutine(selfRef.DoSetLeaderboardData(leaderboardId, deviceId, nickname, score));
			} else {
				if(selfRef.debugMode)
					Debug.Log("[DEBUG] Failed to submit " + leaderboardId + "! No internet connection");

				if(OnSubmitConnectionFailed != null)
					OnSubmitConnectionFailed.Invoke(leaderboardId);
			}
		} else {
			if(selfRef.debugMode)
				Debug.Log("[DEBUG] Failed to submit " + leaderboardId + " as another submit was still active");

			if(OnSubmitAlreadyPending != null)
				OnSubmitAlreadyPending.Invoke(leaderboardId);
		}
	}

	private IEnumerator DoGetLeaderboardRankData(string leaderboardId, int score, TimePeriod timePeriod = TimePeriod.AllTime, string deviceId = "")
	{
		// Mark the rank as active (being processed)
		rankStorage[leaderboardId].isActive = true;

		string requestURL = "https://data.i6.com/datastore.php?";

		// The queryString variable is setup to match the PHP $_SERVER['QUERY_STRING'] variable
		string queryString = "action=get_leaderboard_rank";
		queryString += "&platform=" + Application.platform.ToString();
		queryString += "&package_name=" + packageName;
		queryString += "&leaderboard=" + leaderboardId;
		queryString += "&score=" + score;
		queryString += "&time=" + timePeriod;
		queryString += "&token=" + WWW.EscapeURL(GetSecurityToken(), Encoding.UTF8);
		queryString += deviceId != string.Empty ? "&device=" + deviceId : "";

		requestURL += queryString;

		// The checksum allows us to validate that the requested URL matches the URL sent to the server
		requestURL += "&checksum=" + WWW.EscapeURL(GenerateChecksum(queryString), Encoding.UTF8);

		if(debugMode)
			Debug.Log("[DEBUG] Send request to: " + requestURL);

		// Request the leaderboard rank data from the server
		WWW leaderboardRankRequest = new WWW(requestURL);

		while(!leaderboardRankRequest.isDone)
			yield return null;

		if(!string.IsNullOrEmpty(leaderboardRankRequest.error)){
			GoogleAnalytics.Instance.LogError("Failed to get leaderboard rank data! " + leaderboardRankRequest.error);

			rankStorage[leaderboardId].isError = true;
			rankStorage[leaderboardId].isActive = false;

			if(debugMode)
				Debug.Log("[DEBUG] Rank failed for " + leaderboardId + " error: " + leaderboardRankRequest.error);

			if(OnRankRequestFailed != null)
				OnRankRequestFailed.Invoke(leaderboardId, leaderboardRankRequest.error);

			yield break;
		}

		string errorResponse;

		if(IsErrorResponse(leaderboardId, leaderboardRankRequest.text, out errorResponse)){
			GoogleAnalytics.Instance.LogError("Failed to get leaderboard rank data! " + errorResponse);

			rankStorage[leaderboardId].isError = true;
			rankStorage[leaderboardId].isActive = false;

			if(debugMode)
				Debug.Log("[DEBUG] Rank failed for " + leaderboardId + " error: " + errorResponse);

			if(OnRankRequestFailed != null)
				OnRankRequestFailed.Invoke(leaderboardId, errorResponse);

			yield break;
		}

		try {
			#if UNITY_5 || UNITY_2017_1_OR_NEWER
				rankStorage[leaderboardId] = JsonUtility.FromJson<RankResponse>(leaderboardRankRequest.text);
			#else
				JSONNode jsonData = JSON.Parse(leaderboardRankRequest.text);
				rankStorage[leaderboardId].response = jsonData["response"].ToString();
			#endif

			rankStorage[leaderboardId].isReady = true;
		} catch(System.Exception e){
			GoogleAnalytics.Instance.LogError("Rank JSON data invalid!" + e.Message, false);

			rankStorage[leaderboardId].isError = true;
			rankStorage[leaderboardId].isActive = false;

			if(debugMode)
				Debug.Log("[DEBUG] Rank failed for " + leaderboardId + " error: " + e.Message);

			if(OnRankRequestFailed != null)
				OnRankRequestFailed.Invoke(leaderboardId, e.Message);

			yield break;
		}

		// Cleanup the WWW request data
		leaderboardRankRequest.Dispose();

		rankStorage[leaderboardId].isReady = true;
		rankStorage[leaderboardId].isActive = false;

		if(debugMode)
			Debug.Log("[DEBUG] Rank ready for " + leaderboardId + " rank is " + rankStorage[leaderboardId].response);

		// Trigger the OnLeaderboardRankReady action
		if(OnRankDone != null)
			OnRankDone.Invoke(leaderboardId, rankStorage[leaderboardId]);
	}

	private IEnumerator DoGetLeaderboardData(string leaderboardId, string deviceId = "", TimePeriod timePeriod = TimePeriod.AllTime, int pageNum = 0)
	{
		// Mark the rank as active (being processed)
		leaderboardStorage[leaderboardId].isActive = true;

		string requestURL = "https://data.i6.com/datastore.php?";

		// The queryString variable is setup to match the PHP $_SERVER['QUERY_STRING'] variable
		string queryString = "action=get_leaderboard";
		queryString += "&platform=" + Application.platform.ToString();
		queryString += "&package_name=" + packageName;
		queryString += "&leaderboard=" + leaderboardId;
		queryString += "&time=" + timePeriod;
		queryString += "&page=" + pageNum;
		queryString += "&perpage=" + resultsPerPage;
		queryString += "&token=" + WWW.EscapeURL(GetSecurityToken(), Encoding.UTF8);
		queryString += deviceId != string.Empty ? "&device=" + deviceId : "";

		requestURL += queryString;

		// The checksum allows us to validate that the requested URL matches the URL sent to the server
		requestURL += "&checksum=" + WWW.EscapeURL(GenerateChecksum(queryString), Encoding.UTF8);

		if(debugMode)
			Debug.Log("[DEBUG] Send request to: " + requestURL);

		// Request the leaderboard data from the server
		WWW leaderboardRequest = new WWW(requestURL);

		while(!leaderboardRequest.isDone)
			yield return null;

		if(!string.IsNullOrEmpty(leaderboardRequest.error)){
			GoogleAnalytics.Instance.LogError("Failed to get leaderboard data! " + leaderboardRequest.error);

			leaderboardStorage[leaderboardId].isError = true;
			leaderboardStorage[leaderboardId].isActive = false;

			if(debugMode)
				Debug.Log("[DEBUG] Leaderboard failed for " + leaderboardId + " error: " + leaderboardRequest.error);

			if(OnLeaderboardRequestFailed != null)
				OnLeaderboardRequestFailed.Invoke(leaderboardId, leaderboardRequest.error);

			yield break;
		}

		string errorResponse;

		if(IsErrorResponse(leaderboardId, leaderboardRequest.text, out errorResponse)){
			GoogleAnalytics.Instance.LogError("Failed to get leaderboard data! " + errorResponse);

			leaderboardStorage[leaderboardId].isError = true;
			leaderboardStorage[leaderboardId].isActive = false;

			if(debugMode)
				Debug.Log("[DEBUG] Leaderboard failed for " + leaderboardId + " error: " + errorResponse);

			if(OnLeaderboardRequestFailed != null)
				OnLeaderboardRequestFailed.Invoke(leaderboardId, errorResponse);

			yield break;
		}

		try {
			#if UNITY_5_3_OR_NEWER
				leaderboardStorage[leaderboardId] = JsonUtility.FromJson<LeaderboardResponse>(leaderboardRequest.text);
			#else
				leaderboardStorage[leaderboardId].response = new List<LeaderboardStorage>();
				JSONNode jsonData = JSON.Parse(leaderboardRequest.text);

				// Iterate through each row of the leaderboard adding the results to the class storage
				for(int rowId=0;rowId < jsonData["response"].AsArray.Count;rowId++)
				{
					JSONNode row = jsonData["response"].AsArray[rowId];
					LeaderboardStorage leaderboardRowData = new LeaderboardStorage();

					leaderboardRowData.device_identifier = row["device_identifier"];
					leaderboardRowData.nickname = row["nickname"];
					leaderboardRowData.score = int.Parse(row["score"]);
					leaderboardRowData.timestamp = long.Parse(row["timestamp"]);

					leaderboardStorage[leaderboardId].response.Add(leaderboardRowData);
				}
			#endif

			leaderboardStorage[leaderboardId].isReady = true;
		} catch(System.Exception e){
			GoogleAnalytics.Instance.LogError("Leaderboard JSON data invalid!" + e.Message, false);

			leaderboardStorage[leaderboardId].isError = true;
			leaderboardStorage[leaderboardId].isActive = false;

			if(debugMode)
				Debug.Log("[DEBUG] Leaderboard failed for " + leaderboardId + " error: " + e.Message);

			if(OnLeaderboardRequestFailed != null)
				OnLeaderboardRequestFailed.Invoke(leaderboardId, e.Message);
			yield break;
		}

		// Cleanup the WWW request data
		leaderboardRequest.Dispose();

		leaderboardStorage[leaderboardId].isReady = true;
		leaderboardStorage[leaderboardId].isActive = false;

		if(debugMode)
			Debug.Log("[DEBUG] Leaderboard ready for " + leaderboardId + " found " + leaderboardStorage[leaderboardId].Count() + " rows");

		// Trigger the OnLeaderboardReady action
		if(OnLeaderboardDone != null)
			OnLeaderboardDone.Invoke(leaderboardId, leaderboardStorage[leaderboardId]);
	}

	// Setting the leaderboard doesn't touch the Leaderboards[..] data just incase we're reading at the same time as submitting
	private IEnumerator DoSetLeaderboardData(string leaderboardId, string deviceId, string nickname, int score)
	{
		isScoreSubmitting = true;

		string requestURL = "https://data.i6.com/datastore.php?";

		// The queryString variable is setup to match the PHP $_SERVER['QUERY_STRING'] variable
		string queryString = "action=set_leaderboard";
		queryString += "&platform=" + Application.platform.ToString();
		queryString += "&package_name=" + packageName;
		queryString += "&leaderboard=" + leaderboardId;
		queryString += "&device=" + deviceId;
		queryString += "&nickname=" + nickname;
		queryString += "&score=" + score;
		queryString += "&perpage=" + resultsPerPage;
		queryString += "&token=" + WWW.EscapeURL(GetSecurityToken(), Encoding.UTF8);

		requestURL += queryString;

		// The checksum allows us to validate that the requested URL matches the URL sent to the server
		requestURL += "&checksum=" + WWW.EscapeURL(GenerateChecksum(queryString), Encoding.UTF8);

		if(debugMode)
			Debug.Log("[DEBUG] Send request to: " + requestURL);

		// Send the request to add this data to the leaderboard
		WWW leaderboardRequest = new WWW(requestURL);

		while(!leaderboardRequest.isDone)
			yield return null;

		if(!string.IsNullOrEmpty(leaderboardRequest.error)){
			GoogleAnalytics.Instance.LogError("Failed to submit leaderboard! " + leaderboardRequest.error);

			isScoreSubmitting = false;

			if(debugMode)
				Debug.Log("[DEBUG] Submit failed for " + leaderboardId + " error: " + leaderboardRequest.error);

			if(OnSubmitRequestFailed != null)
				OnSubmitRequestFailed.Invoke(leaderboardId, leaderboardRequest.error);

			yield break;
		}

		string errorResponse;

		if(IsErrorResponse(leaderboardId, leaderboardRequest.text, out errorResponse)){
			GoogleAnalytics.Instance.LogError("Failed to submit leaderboard! " + errorResponse);

			isScoreSubmitting = false;

			if(debugMode)
				Debug.Log("[DEBUG] Submit failed for " + leaderboardId + " error: " + errorResponse);

			if(OnSubmitRequestFailed != null)
				OnSubmitRequestFailed.Invoke(leaderboardId, errorResponse);

			yield break;
		}

		// Cleanup the WWW request data
		leaderboardRequest.Dispose();

		isScoreSubmitting = false;

		if(debugMode)
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
