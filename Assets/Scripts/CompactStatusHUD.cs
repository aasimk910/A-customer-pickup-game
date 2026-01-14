using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Compact Status HUD - Professional diagnostics panel for Camera 4 overlay.
/// Dark theme with green accents, positioned in bottom-right corner.
/// Non-intrusive picture-in-picture style display.
/// </summary>
public class CompactStatusHUD : MonoBehaviour
{
    [Header("Agents")]
    public List<AgentStatsSource> agents = new List<AgentStatsSource>();

    [Header("ACO Manager Reference")]
    public ACOManager acoManager;

    [Header("Camera")]
    [Tooltip("Target camera (Camera 4)")]
    public Camera targetCamera;

    [Header("Panel Position")]
    [Tooltip("Position from bottom-right corner")]
    [Range(0f, 0.05f)] public float marginRight = 0.02f;
    [Range(0f, 0.05f)] public float marginBottom = 0.02f;
    
    [Header("Panel Size")]
    [Range(0.25f, 1.0f)] public float panelWidthPercent = 0.35f;
    [Tooltip("Manual panel height in pixels (0 = auto-calculate based on content)")]
    [Range(0f, 540f)] public float manualPanelHeight = 0f;

    // Colors - Dark theme with green accents
    private readonly Color panelBgColor = new Color(0.08f, 0.08f, 0.10f, 0.92f);
    private readonly Color headerBgColor = new Color(0.12f, 0.12f, 0.14f, 1f);
    private readonly Color accentColor = new Color(0.2f, 0.8f, 0.4f, 1f);
    private readonly Color accentDim = new Color(0.15f, 0.5f, 0.3f, 1f);
    private readonly Color textPrimary = new Color(0.95f, 0.95f, 0.95f, 1f);
    private readonly Color textSecondary = new Color(0.65f, 0.65f, 0.70f, 1f);
    private readonly Color dividerColor = new Color(0.25f, 0.25f, 0.28f, 1f);
    private readonly Color statusGood = new Color(0.3f, 0.85f, 0.5f, 1f);
    private readonly Color statusWarning = new Color(0.95f, 0.75f, 0.2f, 1f);
    private readonly Color statusActive = new Color(0.3f, 0.7f, 1f, 1f);

    // Textures
    private Texture2D panelBgTex;
    private Texture2D headerBgTex;
    private Texture2D accentTex;
    private Texture2D dividerTex;

    // Styles
    private GUIStyle titleStyle;
    private GUIStyle paramLabelStyle;
    private GUIStyle paramValueStyle;
    private GUIStyle tableHeaderStyle;
    private GUIStyle tableCellStyle;
    private GUIStyle statusStyle;
    private GUIStyle sectionStyle;
    private GUIStyle timerStyle;
    private bool stylesInit = false;

    // Runtime
    private float elapsedTime = 0f;
    private Dictionary<AgentStatsSource, ACOTester> acoTesters = new Dictionary<AgentStatsSource, ACOTester>();

    void Start()
    {
        if (acoManager == null)
            acoManager = FindObjectOfType<ACOManager>();

        // Find Camera 4 specifically if not assigned
        if (targetCamera == null)
        {
            // First try to find camera named "Camera 4"
            GameObject cam4Obj = GameObject.Find("Camera 4");
            if (cam4Obj != null)
                targetCamera = cam4Obj.GetComponent<Camera>();
            
            // Fallback to attached camera
            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();
        }

        foreach (var agent in agents)
        {
            if (agent != null)
            {
                var acoTester = agent.GetComponent<ACOTester>();
                if (acoTester != null)
                    acoTesters[agent] = acoTester;
            }
        }

        CreateTextures();
    }

    void Update()
    {
        elapsedTime += Time.deltaTime;
    }

    void CreateTextures()
    {
        panelBgTex = MakeTex(2, 2, panelBgColor);
        headerBgTex = MakeTex(2, 2, headerBgColor);
        accentTex = MakeTex(2, 2, accentColor);
        dividerTex = MakeTex(2, 2, dividerColor);
    }

    Texture2D MakeTex(int w, int h, Color col)
    {
        Texture2D tex = new Texture2D(w, h);
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    void InitStyles()
    {
        if (stylesInit) return;
        stylesInit = true;

        // Title - Bold, accent color
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        titleStyle.normal.textColor = accentColor;

        // Timer style
        timerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleRight
        };
        timerStyle.normal.textColor = textSecondary;

        // Parameter label
        paramLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };
        paramLabelStyle.normal.textColor = textSecondary;

        // Parameter value
        paramValueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        paramValueStyle.normal.textColor = accentColor;

        // Table header
        tableHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        tableHeaderStyle.normal.textColor = textSecondary;

        // Table cell
        tableCellStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleLeft
        };
        tableCellStyle.normal.textColor = textPrimary;

        // Status text
        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleLeft
        };
        statusStyle.normal.textColor = statusGood;

        // Section header
        sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        sectionStyle.normal.textColor = accentDim;
    }

    void OnGUI()
    {
        InitStyles();

        // Camera 4 quadrant for 1920x1080 (bottom-right)
        float screenW = Screen.width;
        float screenH = Screen.height;
        float camW = screenW / 2f;
        float camH = screenH / 2f;
        
        // Camera 4 area in GUI coordinates (bottom-right quadrant)
        Rect camera4Area = new Rect(camW, camH, camW, camH);
        
        // Clip all GUI drawing to Camera 4 area
        GUI.BeginGroup(camera4Area);
        
        // Now use local coordinates (0,0 is top-left of Camera 4 area)
        Rect viewport = new Rect(0, 0, camW, camH);
        
        // Layout constants
        float padding = 15f;
        float rowH = 28f;
        float smallRowH = 24f;
        float paramRowH = 40f;
        float dividerGap = 8f;
        
        // Calculate required height based on content
        float requiredHeight = padding * 2; // Top and bottom padding
        requiredHeight += rowH + 4; // Title row
        requiredHeight += dividerGap; // Divider
        if (acoManager != null)
        {
            requiredHeight += smallRowH; // PARAMETERS label
            requiredHeight += paramRowH + 8; // Parameter values
        }
        requiredHeight += dividerGap; // Divider
        requiredHeight += smallRowH + 2; // AGENTS label
        requiredHeight += smallRowH + 2; // Table header
        requiredHeight += (smallRowH + 6) * Mathf.Max(agents.Count, 1); // Agent rows
        requiredHeight += 8; // Gap
        requiredHeight += dividerGap; // Divider
        requiredHeight += smallRowH; // COLLISION label
        requiredHeight += smallRowH + 10; // Collision text
        
        // Calculate panel dimensions
        float panelW = viewport.width * panelWidthPercent;
        // Use manual height if set, otherwise auto-calculate
        float panelH = manualPanelHeight > 0 ? manualPanelHeight : requiredHeight;
        float panelX = viewport.x + viewport.width - panelW - (viewport.width * marginRight);
        float panelY = viewport.y + viewport.height - panelH - (viewport.height * marginBottom);

        // Panel background
        Rect panelRect = new Rect(panelX, panelY, panelW, panelH);
        GUI.DrawTexture(panelRect, panelBgTex);

        // Draw accent bar on left side
        GUI.DrawTexture(new Rect(panelX, panelY, 4, panelH), accentTex);

        float innerX = panelX + padding + 4; // +4 for accent bar
        float innerW = panelW - (padding * 2) - 4;
        float y = panelY + padding;

        // === HEADER ===
        // Title + Timer
        GUI.Label(new Rect(innerX, y, innerW * 0.7f, rowH), "● ACO DIAGNOSTICS", titleStyle);
        GUI.Label(new Rect(innerX + innerW * 0.7f, y, innerW * 0.3f, rowH), FormatTime(elapsedTime), timerStyle);
        y += rowH + 4;

        // Divider
        GUI.DrawTexture(new Rect(innerX, y, innerW, 1), dividerTex);
        y += 6;

        // === ACO PARAMETERS ===
        if (acoManager != null)
        {
            GUI.Label(new Rect(innerX, y, innerW, smallRowH), "PARAMETERS", sectionStyle);
            y += smallRowH;

            // Parameter grid - 5 columns
            float paramW = innerW / 5f;
            DrawParameter(innerX, y, paramW, "α", $"{acoManager.Alpha:0.0}");
            DrawParameter(innerX + paramW, y, paramW, "β", $"{acoManager.Beta:0.0}");
            DrawParameter(innerX + paramW * 2, y, paramW, "Q", $"{acoManager.QValue:0}");
            DrawParameter(innerX + paramW * 3, y, paramW, "ρ", $"{acoManager.EvaporationFactor:0.0}");
            DrawParameter(innerX + paramW * 4, y, paramW, "P", $"{acoManager.DefaultPheromone:0.0}");
            y += rowH + 8;
        }

        // Divider
        GUI.DrawTexture(new Rect(innerX, y, innerW, 1), dividerTex);
        y += 6;

        // === AGENTS TABLE ===
        GUI.Label(new Rect(innerX, y, innerW, smallRowH), "AGENTS", sectionStyle);
        y += smallRowH + 2;

        // Table header
        float[] cols = { 0.28f, 0.12f, 0.15f, 0.15f, 0.30f };
        string[] headers = { "NAME", "PKG", "SPD", "DIST", "STATUS" };
        
        float colX = innerX;
        for (int i = 0; i < headers.Length; i++)
        {
            float colW = innerW * cols[i];
            GUI.Label(new Rect(colX, y, colW, smallRowH), headers[i], tableHeaderStyle);
            colX += colW;
        }
        y += smallRowH + 2;

        // Agent rows
        foreach (var agent in agents)
        {
            if (agent == null) continue;

            ACOTester acoTester = null;
            acoTesters.TryGetValue(agent, out acoTester);

            colX = innerX;

            // Name (with status indicator)
            string name = GetShortName(agent);
            bool isActive = acoTester != null && acoTester.IsMoving;
            
            GUIStyle nameStyle = new GUIStyle(tableCellStyle);
            nameStyle.normal.textColor = isActive ? statusActive : textPrimary;
            
            string indicator = isActive ? "▸ " : "  ";
            GUI.Label(new Rect(colX, y, innerW * cols[0], smallRowH), indicator + name, nameStyle);
            colX += innerW * cols[0];

            // Check if ACOTester is enabled (not just exists)
            bool acoActive = acoTester != null && acoTester.enabled;

            // Packages
            int pkgs = acoActive ? acoTester.CurrentParcelCount : agent.packageCount;
            GUI.Label(new Rect(colX, y, innerW * cols[1], smallRowH), $"{pkgs}", tableCellStyle);
            colX += innerW * cols[1];

            // Speed - use agent.speedMS which is updated by both ACOTester and PathfindingTester
            float spd = agent.speedMS;
            GUIStyle spdStyle = new GUIStyle(tableCellStyle);
            spdStyle.normal.textColor = spd > 0.1f ? statusGood : textSecondary;
            GUI.Label(new Rect(colX, y, innerW * cols[2], smallRowH), $"{spd:0.0}", spdStyle);
            colX += innerW * cols[2];

            // Distance
            float dist = acoActive ? acoTester.TotalDistanceTravelled : agent.totalDistanceM;
            GUI.Label(new Rect(colX, y, innerW * cols[3], smallRowH), $"{dist:0}", tableCellStyle);
            colX += innerW * cols[3];

            // Status
            string status = acoActive ? acoTester.CurrentStatus : agent.deliveryStatus;
            if (string.IsNullOrEmpty(status)) status = "—";
            status = TruncateStatus(status, 20);
            
            GUIStyle statStyle = new GUIStyle(statusStyle);
            statStyle.normal.textColor = GetStatusColor(status);
            GUI.Label(new Rect(colX, y, innerW * cols[4], smallRowH), status, statStyle);

            y += smallRowH + 6;
        }

        y += 8;

        // Divider
        GUI.DrawTexture(new Rect(innerX, y, innerW, 1), dividerTex);
        y += 6;

        // === COLLISION INFO ===
        GUI.Label(new Rect(innerX, y, innerW, smallRowH), "COLLISION", sectionStyle);
        y += smallRowH;

        string collision = AgentStatsSource.lastCollisionMessage;
        if (string.IsNullOrEmpty(collision)) collision = "No collision detected";
        
        GUIStyle collisionStyle = new GUIStyle(tableCellStyle);
        collisionStyle.fontSize = 14;
        collisionStyle.normal.textColor = collision.Contains("No collision") ? textSecondary : statusWarning;
        GUI.Label(new Rect(innerX, y, innerW, smallRowH), collision, collisionStyle);
        
        // End the Camera 4 clipping group
        GUI.EndGroup();
    }

    void DrawParameter(float x, float y, float w, string label, string value)
    {
        float halfH = 16f;
        GUI.Label(new Rect(x, y, w, halfH), label, paramLabelStyle);
        GUI.Label(new Rect(x, y + halfH, w, halfH + 4), value, paramValueStyle);
    }

    string GetShortName(AgentStatsSource agent)
    {
        string name = string.IsNullOrWhiteSpace(agent.agentName) ? agent.gameObject.name : agent.agentName;
        // Remove common prefixes
        name = name.Replace("Taxi", "").Replace("Agent", "").Replace("Car", "").Trim();
        if (name.Length > 8) name = name.Substring(0, 8);
        return name;
    }

    string TruncateStatus(string status, int maxLen)
    {
        if (status.Length <= maxLen) return status;
        return status.Substring(0, maxLen - 1) + "…";
    }

    Color GetStatusColor(string status)
    {
        if (status.Contains("Picking") || status.Contains("Loading")) return statusWarning;
        if (status.Contains("Yielding") || status.Contains("Waiting")) return statusWarning;
        if (status.Contains("Returning") || status.Contains("A*")) return statusActive;
        if (status.Contains("picked") || status.Contains("complete")) return statusGood;
        return textSecondary;
    }

    string FormatTime(float t)
    {
        int mins = (int)(t / 60f);
        int secs = (int)(t % 60f);
        return $"{mins:00}:{secs:00}";
    }

    Rect GetViewportRect()
    {
        // For 1920x1080 with 2x2 camera grid, Camera 4 is bottom-right quadrant
        // Each camera is 960x540
        // Camera 4 position: x=960, y=540 (GUI coordinates with Y from top)
        float screenW = 1920f;
        float screenH = 1080f;
        float camW = screenW / 2f;  // 960
        float camH = screenH / 2f;  // 540
        
        // Camera 4 is bottom-right: x=960, y=540 in GUI coordinates
        return new Rect(camW, camH, camW, camH);
    }

    void OnDestroy()
    {
        if (panelBgTex != null) Destroy(panelBgTex);
        if (headerBgTex != null) Destroy(headerBgTex);
        if (accentTex != null) Destroy(accentTex);
        if (dividerTex != null) Destroy(dividerTex);
    }
}
