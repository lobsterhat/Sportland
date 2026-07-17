using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Sportland.Diagnostics
{
    /// <summary>
    /// Ask questions in plain English about what the physics just did.
    ///
    /// Talks straight to the Anthropic Messages API over UnityWebRequest — no
    /// SDK package, no external DLLs, nothing new in the manifest.
    ///
    /// EDITOR/DEV BUILDS ONLY. The API key lives in the environment, not in the
    /// project. Shipping a client build with an embedded key hands your key to
    /// anyone who unzips the game.
    /// </summary>
    public class PhysicsDebugAssistant : MonoBehaviour
    {
        [Tooltip("Environment variable holding the API key. Never hardcode the key itself.")]
        public string apiKeyEnvVar = "ANTHROPIC_API_KEY";

        [Tooltip("Model to ask.")]
        public string model = "claude-opus-4-8";

        [Tooltip("Seconds before giving up on a response.")]
        public float timeoutSeconds = 60f;

        private const string Endpoint = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion = "2023-06-01";

        private const string SystemPrompt =
            "You are debugging a Unity 2D arcade sports game with a developer. " +
            "You'll get a physics transcript — a rolling window of body state " +
            "sampled at fixed intervals, plus discrete game events.\n\n" +
            "Answer the developer's question using the numbers in the transcript. " +
            "Cite specific values and timestamps. If the data doesn't show what " +
            "they're asking about, say so plainly and name what would need to be " +
            "tracked to answer it — don't speculate past the data.\n\n" +
            "Be concise. This is a debugging session, not a lecture.";

        private string _apiKey = null;   // only ever set in editor/dev builds

        public bool IsBusy { get; private set; }
        public string LastAnswer { get; private set; }
        public string LastError { get; private set; }

        void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _apiKey = Environment.GetEnvironmentVariable(apiKeyEnvVar);
            if (string.IsNullOrEmpty(_apiKey))
            {
                Debug.LogWarning(
                    $"[PhysicsDebugAssistant] No API key in ${apiKeyEnvVar}. " +
                    "Assistant disabled. Set the env var and restart the editor.");
                enabled = false;
            }
#else
            // Hard stop: this component has no business in a release build.
            Destroy(this);
#endif
        }

        /// <summary>
        /// Fire a question at the model. Returns immediately; the answer lands in
        /// LastAnswer. Non-blocking by design — the game keeps running.
        /// </summary>
        public void Ask(string question)
        {
            if (IsBusy)
            {
                Debug.Log("[PhysicsDebugAssistant] Already waiting on an answer.");
                return;
            }
            if (string.IsNullOrEmpty(_apiKey))
            {
                LastError = $"No API key in ${apiKeyEnvVar}.";
                return;
            }
            if (PhysicsRecorder.Instance == null)
            {
                Debug.LogError("[PhysicsDebugAssistant] No PhysicsRecorder in the scene.");
                return;
            }

            // Snapshot before the request goes out so the transcript reflects
            // the moment the question was asked, not the moment it resolved.
            string transcript = PhysicsRecorder.Instance.BuildTranscript();
            StartCoroutine(SendRequest(question, transcript));
        }

        private IEnumerator SendRequest(string question, string transcript)
        {
            IsBusy = true;
            LastError = null;
            LastAnswer = null;

            string body = JsonUtility.ToJson(new ApiRequest
            {
                model = model,
                max_tokens = 2000,
                system = SystemPrompt,
                thinking = new ApiThinking { type = "adaptive" },
                messages = new[]
                {
                    new ApiMessage { role = "user", content = $"{transcript}\n\n---\n\nQuestion: {question}" },
                },
            });

            using (var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("content-type", "application/json");
                req.SetRequestHeader("x-api-key", _apiKey);
                req.SetRequestHeader("anthropic-version", ApiVersion);
                req.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    LastAnswer = ExtractText(req.downloadHandler.text);
                    Debug.Log($"[PhysicsDebugAssistant] Q: {question}\n\nA: {LastAnswer}");
                }
                else
                {
                    LastError = DescribeFailure(req);
                    Debug.LogWarning($"[PhysicsDebugAssistant] Request failed: {LastError}");
                }
            }

            IsBusy = false;
        }

        // Response content is a list of blocks; the answer is the text blocks
        // joined (thinking blocks etc. are skipped).
        private static string ExtractText(string json)
        {
            var response = JsonUtility.FromJson<ApiResponse>(json);
            if (response?.content == null || response.content.Length == 0)
                return "(empty response)";

            var sb = new StringBuilder();
            foreach (var block in response.content)
                if (block.type == "text" && !string.IsNullOrEmpty(block.text))
                    sb.Append(block.text);
            return sb.Length > 0 ? sb.ToString() : "(no text content in response)";
        }

        private string DescribeFailure(UnityWebRequest req)
        {
            if (req.result == UnityWebRequest.Result.ConnectionError && req.error != null
                && req.error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                return $"Timed out after {timeoutSeconds}s.";

            // API errors come back as {"type":"error","error":{"type","message"}}.
            string raw = req.downloadHandler != null ? req.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    var err = JsonUtility.FromJson<ApiErrorEnvelope>(raw);
                    if (!string.IsNullOrEmpty(err?.error?.message))
                        return $"{err.error.type}: {err.error.message}";
                }
                catch (ArgumentException) { /* not JSON — fall through */ }
            }
            return $"HTTP {req.responseCode}: {req.error}";
        }

        // ── Wire shapes (JsonUtility) ───────────────────────────────────

        [Serializable] private class ApiRequest
        {
            public string model;
            public int max_tokens;
            public string system;
            public ApiThinking thinking;
            public ApiMessage[] messages;
        }

        [Serializable] private class ApiThinking { public string type; }
        [Serializable] private class ApiMessage { public string role; public string content; }

        [Serializable] private class ApiResponse { public ApiContentBlock[] content; }
        [Serializable] private class ApiContentBlock { public string type; public string text; }

        [Serializable] private class ApiErrorEnvelope { public ApiError error; }
        [Serializable] private class ApiError { public string type; public string message; }
    }
}
