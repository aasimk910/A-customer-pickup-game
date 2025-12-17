using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class LiveStatusReportRuntimeUI : MonoBehaviour
{
    [Header("Agents (Drag 3 cars' AgentStatsSource components here)")]
    public List<AgentStatsSource> agents = new List<AgentStatsSource>();

    [Header("Full Camera 4 View")]
    public Vector2 panelAnchorMin = new Vector2(0f, 0f);
    public Vector2 panelAnchorMax = new Vector2(1f, 1f);

    [Header("Style")]
    public Color backgroundColor = new Color(0.0f, 0.45f, 0.0f, 0.92f);

    [Header("Font Sizes")]
    public int titleFontMax = 60;
    public int headerFontMax = 44;
    public int rowFontMax = 44;

    [Header("Row Heights (prevents invisible text)")]
    public float titleHeight = 72f;
    public float headerRowHeight = 56f;
    public float dataRowHeight = 56f;
    public float collisionTitleHeight = 56f;
    public float collisionValueHeight = 84f;

    // Agent, Speed, Distance, Customer, Status
    readonly float[] colFlex = { 2.2f, 1.4f, 2.1f, 1.0f, 2.6f };

    class RowRefs
    {
        public AgentStatsSource source;
        public TMP_Text agent, speed, distance, packageCount, status;
    }

    RectTransform rootPanel;
    TMP_Text collisionValue;
    readonly List<RowRefs> rows = new List<RowRefs>();

    // Cache last known speeds so even if they stop after collision, UI can still decide "slower"
    readonly Dictionary<AgentStatsSource, float> lastKnownSpeed = new Dictionary<AgentStatsSource, float>();

    void Start()
    {
        // Must be on the Canvas object (the one with Canvas component)
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[LiveStatusReportRuntimeUI] Put this script on the Canvas object (Camera 4 Canvas).");
            enabled = false;
            return;
        }

        // Remove old panel if exists
        var existing = transform.Find("LiveStatusPanel");
        if (existing != null) Destroy(existing.gameObject);

        BuildUI();
        Canvas.ForceUpdateCanvases(); // important for layout
    }

    void Update()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var rr = rows[i];
            if (rr?.source == null) continue;

            var a = rr.source;

            // Keep last known speed updated
            lastKnownSpeed[a] = a.speedMS;

            rr.agent.text = string.IsNullOrWhiteSpace(a.agentName) ? a.gameObject.name : a.agentName;
            rr.speed.text = $"{a.speedMS:0.00} m/s";
            rr.distance.text = $"{a.totalDistanceM:0.0} m";
            rr.packageCount.text = $"{a.packageCount}";
            rr.status.text = string.IsNullOrWhiteSpace(a.deliveryStatus) ? "-" : a.deliveryStatus;
        }

        if (collisionValue != null)
            collisionValue.text = FormatCollisionText(AgentStatsSource.lastCollisionMessage);
    }

    // UI-only override:
    // If your system writes messages like "YellowTaxi collided with RedTaxi",
    // this shows "SLOWER_AGENT moved right to avoid collision".
    string FormatCollisionText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "No Collision";

        var msg = raw.Trim();

        if (msg.Equals("No Collision", StringComparison.OrdinalIgnoreCase))
            return "No Collision";

        const string token = "collided with";
        var idx = msg.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return msg;

        var nameA = SanitizeName(msg.Substring(0, idx).Trim());
        var nameB = SanitizeName(msg.Substring(idx + token.Length).Trim());

        AgentStatsSource srcA = null, srcB = null;
        TryFindAgentByName(nameA, out srcA);
        TryFindAgentByName(nameB, out srcB);

        AgentStatsSource slower = null;

        if (srcA != null && srcB != null)
        {
            float spA = lastKnownSpeed.TryGetValue(srcA, out var vA) ? vA : srcA.speedMS;
            float spB = lastKnownSpeed.TryGetValue(srcB, out var vB) ? vB : srcB.speedMS;
            slower = (spA <= spB) ? srcA : srcB;
        }
        else if (srcA != null)
        {
            slower = srcA;
        }
        else if (srcB != null)
        {
            slower = srcB;
        }

        var slowerName = slower != null ? GetDisplayName(slower, "Agent") : (!string.IsNullOrWhiteSpace(nameA) ? nameA : "Agent");
        return $"{slowerName} moved right to avoid collision";
    }

    string GetDisplayName(AgentStatsSource src, string fallback)
    {
        if (src == null) return fallback;
        if (!string.IsNullOrWhiteSpace(src.agentName)) return src.agentName;
        if (src.gameObject != null && !string.IsNullOrWhiteSpace(src.gameObject.name)) return src.gameObject.name;
        return fallback;
    }

    string SanitizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        s = s.Trim();

        // Remove common trailing punctuation Unity messages might include
        while (s.Length > 0)
        {
            char c = s[s.Length - 1];
            if (c == '.' || c == ',' || c == ';' || c == ':' || c == '!' || c == '?' || c == ')' || c == ']' || c == '}')
                s = s.Substring(0, s.Length - 1).TrimEnd();
            else
                break;
        }

        // Also remove leading punctuation
        while (s.Length > 0)
        {
            char c = s[0];
            if (c == '(' || c == '[' || c == '{')
                s = s.Substring(1).TrimStart();
            else
                break;
        }

        return s;
    }

    bool TryFindAgentByName(string name, out AgentStatsSource found)
    {
        found = null;
        if (string.IsNullOrWhiteSpace(name)) return false;

        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            if (a == null) continue;

            // Match against agentName OR GameObject name
            if (!string.IsNullOrWhiteSpace(a.agentName) &&
                string.Equals(a.agentName.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                found = a;
                return true;
            }

            if (a.gameObject != null &&
                string.Equals(a.gameObject.name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                found = a;
                return true;
            }
        }

        return false;
    }

    void BuildUI()
    {
        rootPanel = CreateRect("LiveStatusPanel", transform);
        rootPanel.SetAsLastSibling();

        var img = rootPanel.gameObject.AddComponent<Image>();
        img.color = backgroundColor;

        rootPanel.anchorMin = panelAnchorMin;
        rootPanel.anchorMax = panelAnchorMax;
        rootPanel.offsetMin = Vector2.zero;
        rootPanel.offsetMax = Vector2.zero;

        var vlg = rootPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 16, 16);
        vlg.spacing = 10;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Title (force height)
        var titleHolder = CreateRect("TitleHolder", rootPanel);
        titleHolder.gameObject.AddComponent<LayoutElement>().preferredHeight = titleHeight;

        var title = CreateTMP("Title", titleHolder, "Live Status Report", true);
        SetupText(title, titleFontMax, 32, TextAlignmentOptions.Center, singleLine: true);

        // Table container
        var table = CreateRect("Table", rootPanel);
        var tableV = table.gameObject.AddComponent<VerticalLayoutGroup>();
        tableV.spacing = 8;
        tableV.childControlWidth = true;
        tableV.childControlHeight = true;
        tableV.childForceExpandWidth = true;
        tableV.childForceExpandHeight = false;

        CreateRow(table, isHeader: true);

        rows.Clear();
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] == null)
            {
                Debug.LogWarning($"[LiveStatusReportRuntimeUI] Agents[{i}] is NULL. Drag AgentStatsSource from Hierarchy.");
                continue;
            }
            rows.Add(CreateRow(table, isHeader: false, source: agents[i]));
        }

        // Collision title (force height)
        var colTitleHolder = CreateRect("CollisionTitleHolder", rootPanel);
        colTitleHolder.gameObject.AddComponent<LayoutElement>().preferredHeight = collisionTitleHeight;

        var colTitle = CreateTMP("CollisionTitle", colTitleHolder, "Collision", true);
        SetupText(colTitle, headerFontMax, 28, TextAlignmentOptions.Center, singleLine: true);

        // Collision value (force height)
        var colValueHolder = CreateRect("CollisionValueHolder", rootPanel);
        colValueHolder.gameObject.AddComponent<LayoutElement>().preferredHeight = collisionValueHeight;

        collisionValue = CreateTMP("CollisionValue", colValueHolder, "No Collision", false);
        SetupText(collisionValue, rowFontMax, 24, TextAlignmentOptions.Center, singleLine: false);
        collisionValue.enableWordWrapping = true;
    }

    RowRefs CreateRow(RectTransform parent, bool isHeader, AgentStatsSource source = null)
    {
        var row = CreateRect(isHeader ? "HeaderRow" : "AgentRow", parent);

        var leRow = row.gameObject.AddComponent<LayoutElement>();
        leRow.preferredHeight = isHeader ? headerRowHeight : dataRowHeight;

        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.padding = new RectOffset(6, 6, 4, 4);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        if (isHeader)
        {
            CreateCell(row, "H_Agent", "Agent", headerFontMax, true, colFlex[0], TextAlignmentOptions.Left, 18);
            CreateCell(row, "H_Speed", "Speed", headerFontMax, true, colFlex[1], TextAlignmentOptions.Right, 18);
            CreateCell(row, "H_Dist", "Distance", headerFontMax, true, colFlex[2], TextAlignmentOptions.Right, 18);
            CreateCell(row, "H_Customer", "Customer", headerFontMax, true, colFlex[3], TextAlignmentOptions.Center, 18);
            CreateCell(row, "H_Stat", "Status", headerFontMax, true, colFlex[4], TextAlignmentOptions.Left, 18);
            return null;
        }

        var rr = new RowRefs { source = source };
        rr.agent = CreateCell(row, "C_Agent", "", rowFontMax, false, colFlex[0], TextAlignmentOptions.Left, 16);
        rr.speed = CreateCell(row, "C_Speed", "", rowFontMax, false, colFlex[1], TextAlignmentOptions.Right, 16);
        rr.distance = CreateCell(row, "C_Dist", "", rowFontMax, false, colFlex[2], TextAlignmentOptions.Right, 16);
        rr.packageCount = CreateCell(row, "C_Customer", "", rowFontMax, false, colFlex[3], TextAlignmentOptions.Center, 16);
        rr.status = CreateCell(row, "C_Stat", "", rowFontMax, false, colFlex[4], TextAlignmentOptions.Left, 16);
        return rr;
    }

    TMP_Text CreateCell(RectTransform row, string name, string text, int maxSize, bool bold, float flex, TextAlignmentOptions align, int minFont)
    {
        var cell = CreateRect(name, row);

        var le = cell.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = flex;
        le.minWidth = 50;

        var tmp = cell.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = Color.white;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;

        SetupText(tmp, maxSize, minFont, align, singleLine: true);
        return tmp;
    }

    void SetupText(TMP_Text tmp, int max, int min, TextAlignmentOptions align, bool singleLine)
    {
        tmp.alignment = align;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = max;
        tmp.fontSizeMin = min;
        tmp.enableWordWrapping = !singleLine;
        tmp.overflowMode = TextOverflowModes.Overflow;
    }

    RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.localScale = Vector3.one;

        // IMPORTANT for layout groups: don't stretch full height
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, 10);
        rt.anchoredPosition = Vector2.zero;

        return rt;
    }

    TMP_Text CreateTMP(string name, Transform parent, string text, bool bold)
    {
        var rt = CreateRect(name, parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = Color.white;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        return tmp;
    }
}
