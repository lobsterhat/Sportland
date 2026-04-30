using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// On-screen log for blocking events (engagements + shove outcomes). Mirrors
    /// each entry to the Unity console via Debug.Log so it's also captured in
    /// the editor log file. Self-instantiates the first time Log is called.
    /// </summary>
    public class BlockingAiLog : MonoBehaviour
    {
        private struct Entry
        {
            public string text;
            public float  time;
        }

        private const int   MaxEntries    = 14;
        private const float EntryLifetime = 6f;

        private static BlockingAiLog instance;
        private readonly List<Entry> entries = new List<Entry>(MaxEntries);
        private GUIStyle style;

        public static void Log(string message)
        {
            EnsureInstance();
            instance.AddEntry(message);
            Debug.Log($"[BlockingAI] {message}");
        }

        private static void EnsureInstance()
        {
            if (instance != null) return;
            var go = new GameObject("BlockingAiLog");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BlockingAiLog>();
        }

        private void AddEntry(string text)
        {
            entries.Add(new Entry { text = text, time = Time.time });
            if (entries.Count > MaxEntries) entries.RemoveAt(0);
        }

        private void OnGUI()
        {
            float now = Time.time;
            for (int i = entries.Count - 1; i >= 0; i--)
                if (now - entries[i].time > EntryLifetime)
                    entries.RemoveAt(i);

            if (entries.Count == 0) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 14,
                    alignment = TextAnchor.UpperLeft,
                    richText  = true
                };
            }

            const float lineHeight = 18f;
            const float pad        = 8f;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                float age   = now - e.time;
                float alpha = Mathf.Clamp01(1f - age / EntryLifetime);

                // Black drop-shadow for readability over the field colours.
                Color prev = GUI.color;
                Rect rect  = new Rect(pad, pad + i * lineHeight, 900f, lineHeight);
                GUI.color  = new Color(0f, 0f, 0f, alpha * 0.85f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), e.text, style);
                GUI.color  = new Color(1f, 1f, 1f, alpha);
                GUI.Label(rect, e.text, style);
                GUI.color  = prev;
            }
        }
    }
}
