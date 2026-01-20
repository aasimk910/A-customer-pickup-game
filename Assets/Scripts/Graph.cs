using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optimized graph class using Dictionary for O(1) connection lookups.
/// </summary>
public class Graph
{
    #region Private Fields
    
    // Dictionary for O(1) lookup of connections from a node
    private Dictionary<GameObject, List<Connection>> connectionsByNode = new Dictionary<GameObject, List<Connection>>();
    
    #endregion

    #region Constructor

    public Graph()
    {
    }
    
    #endregion

    #region Public Methods

    // Add connection - O(1) average case
    public void AddConnection(Connection aConnection)
    {
        if (aConnection.FromNode == null) return;
        
        if (!connectionsByNode.TryGetValue(aConnection.FromNode, out List<Connection> connections))
        {
            connections = new List<Connection>();
            connectionsByNode[aConnection.FromNode] = connections;
        }
        connections.Add(aConnection);
    }

    // Get the connections from a node - O(1) lookup
    public List<Connection> GetConnections(GameObject fromNode)
    {
        if (fromNode != null && connectionsByNode.TryGetValue(fromNode, out List<Connection> connections))
        {
            return connections;
        }
        return new List<Connection>();
    }
    
    #endregion
}