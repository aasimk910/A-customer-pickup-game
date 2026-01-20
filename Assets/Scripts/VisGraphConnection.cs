using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Connection to another waypoint node for visibility graph.
/// </summary>
[System.Serializable]
public class VisGraphConnection
{
    #region Serialized Fields
    
    // The to node for this connection.
    [SerializeField]
    private GameObject toNode;
    
    #endregion

    #region Properties
    
    public GameObject ToNode
    {
        get { return toNode; }
    }
    
    #endregion
}