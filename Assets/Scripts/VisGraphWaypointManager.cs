using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VisGraphWaypointManager : MonoBehaviour
{
    // Waypoint type enum (public for ACO/A* access)
    public enum WaypointTypeEnum { Standard, Start, Goal }

    [Header("Waypoint Configuration")]
    [Tooltip("Set this waypoint as Standard, Start, or Goal type")]
    [SerializeField]
    public WaypointTypeEnum waypointType = WaypointTypeEnum.Goal;

    // Public property to check if this is a goal waypoint
    public bool IsGoal => waypointType == WaypointTypeEnum.Goal;
    public bool IsStart => waypointType == WaypointTypeEnum.Start;

    // Allow you to set the waypoint text colour.
    [SerializeField]
    private enum waypointTextColour { Blue, Cyan, Yellow, Green, Red };
    [SerializeField]
    private waypointTextColour WaypointTextColour = waypointTextColour.Blue;

    // List of all connections from this node.
    [Header("Connections")]
    [SerializeField]
    public List<VisGraphConnection> connections = new List<VisGraphConnection>();
    public List<VisGraphConnection> Connections
    {
        get { return connections; }
    }
    // Controls if the node type is displayed in the Unity editor.
    private const bool displayType = false;
    // Used to determine if the waypoint is selected.
    private bool ObjectSelected = false;
    // Text displayed above the node.
    private const bool displayText = true;
    private string infoText = "";
    private Color infoTextColor;
    // Start is called before the first frame update
    void Start()
    {
    }
    // Update is called once per frame
    void Update()
    {
    }
    // Draws debug objects in the editor and during editor play (if option set).
    void OnDrawGizmos()
    {
        // Text displayed above the waypoint.
        infoText = "";
        if (displayType)
        {
            infoText = "Type: " + waypointType.ToString() + " / ";
        }
        infoText += gameObject.name + "\n Connections: " + Connections.Count;
        switch (WaypointTextColour)
        {
            case waypointTextColour.Blue:
                infoTextColor = Color.blue;
                break;
            case waypointTextColour.Cyan:
                infoTextColor = Color.cyan;
                break;
            case waypointTextColour.Yellow:
                infoTextColor = Color.yellow;
                break;
            case waypointTextColour.Green:
                infoTextColor = Color.green;
                break;
            case waypointTextColour.Red:
                infoTextColor = Color.red;
                break;
        }

        // Color based on waypoint type
        if (waypointType == WaypointTypeEnum.Goal)
            infoTextColor = Color.green;
        else if (waypointType == WaypointTypeEnum.Start)
            infoTextColor = Color.cyan;

        DrawWaypointAndConnections(ObjectSelected);
#if UNITY_EDITOR
        if (displayText)
        {
            GUIStyle style = new GUIStyle();
            style.normal.textColor = infoTextColor;
            Handles.Label(transform.position + Vector3.up * 1, infoText, style);
        }
#endif
        ObjectSelected = false;
    }
    // Draws debug objects when an object is selected.
    void OnDrawGizmosSelected()
    {
        ObjectSelected = true;
    }
    // Draws debug objects for the waypoint and connections.
    private void DrawWaypointAndConnections(bool ObjectSelected)
    {
        Color WaypointColor = Color.yellow;
        Color ArrowHeadColor = Color.blue;
        if (ObjectSelected)
        {
            WaypointColor = Color.red;
            ArrowHeadColor = Color.magenta;
        }
        // Draw a yellow sphere at the transform's position
        Gizmos.color = WaypointColor;
        Gizmos.DrawSphere(transform.position, 0.2f);
        // Draw all the connections.
        for (int i = 0; i < Connections.Count; i++)
        {
            if (Connections[i].ToNode != null)
            {
                if (Connections[i].ToNode.Equals(gameObject))
                {
                    infoText = "WARNING - Connection to SELF at element: " + i;
                    infoTextColor = Color.red;
                }
                Vector3 direction = Connections[i].ToNode.transform.position - transform.position;
                DrawConnection(i, transform.position, direction, ArrowHeadColor);
                if (ObjectSelected)
                {
                    // Draw spheres along the line.
                    Gizmos.color = ArrowHeadColor;
                    float dist = direction.magnitude;
                    float pos = dist * 0.1f;
                    Gizmos.DrawSphere(transform.position +
                    (direction.normalized * pos), 0.3f);
                    pos = dist * 0.2f;
                    Gizmos.DrawSphere(transform.position +
                    (direction.normalized * pos), 0.3f);
                    pos = dist * 0.3f;
                    Gizmos.DrawSphere(transform.position +
                    (direction.normalized * pos), 0.3f);
                }
            }
            else
            {
                infoText = "WARNING - Connection is missing at element: " + i;
                infoTextColor = Color.red;
            }
        }
    }
    // This arrow method is based on the example here: https://gist.github.com/MatthewMaker/5293052
    public void DrawConnection(float ConnectionsIndex, Vector3 pos, Vector3 direction,
    Color ArrowHeadColor, float arrowHeadLength = 0.5f, float arrowHeadAngle = 40.0f)
    {
        Debug.DrawRay(pos, direction, Color.blue);
        Vector3 right = Quaternion.LookRotation(direction) *
        Quaternion.Euler(0, 180 + arrowHeadAngle, 0) * new Vector3(0, 0, 1);
        Vector3 left = Quaternion.LookRotation(direction) *
        Quaternion.Euler(0, 180 - arrowHeadAngle, 0) * new Vector3(0, 0, 1);
        Debug.DrawRay(pos + direction.normalized +
        (direction.normalized * (0.1f * ConnectionsIndex)),
        right * arrowHeadLength, ArrowHeadColor);
        Debug.DrawRay(pos + direction.normalized +
        (direction.normalized * (0.1f * ConnectionsIndex)),
        left * arrowHeadLength, ArrowHeadColor);
    }
}
