using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optimized pathfinding list using Dictionary for O(1) lookups.
/// </summary>
public class PathfindingList
{
    #region Private Fields
    
    // Dictionary for O(1) Contains and Find operations
    private Dictionary<GameObject, NodeRecord> nodeRecordDict = new Dictionary<GameObject, NodeRecord>();
    // List maintained for GetSmallestElement (could use SortedSet but iteration is rare)
    private List<NodeRecord> nodeRecordList = new List<NodeRecord>();
    
    #endregion

    #region Constructor

    public PathfindingList()
    {
    }
    
    #endregion

    #region List Operations

    // Add NodeRecord - O(1) dictionary insert + O(1) list append
    public void AddNodeRecord(NodeRecord nodeRecord)
    {
        if (nodeRecord.Node != null && !nodeRecordDict.ContainsKey(nodeRecord.Node))
        {
            nodeRecordDict[nodeRecord.Node] = nodeRecord;
            nodeRecordList.Add(nodeRecord);
        }
    }

    // Remove a node from the list - O(1) dictionary remove + O(n) list remove
    public void RemoveNodeRecord(NodeRecord nodeRecord)
    {
        if (nodeRecord.Node != null)
        {
            nodeRecordDict.Remove(nodeRecord.Node);
            nodeRecordList.Remove(nodeRecord);
        }
    }

    // Get the size of the list - O(1)
    public int GetSize()
    {
        return nodeRecordList.Count;
    }
    
    #endregion

    #region Query Operations

    // Get the smallest element - O(n) but unavoidable without more complex data structure
    public NodeRecord GetSmallestElement()
    {
        NodeRecord smallest = null;
        float smallestCost = float.MaxValue;
        
        for (int i = 0; i < nodeRecordList.Count; i++)
        {
            if (nodeRecordList[i].EstimatedTotalCost < smallestCost)
            {
                smallestCost = nodeRecordList[i].EstimatedTotalCost;
                smallest = nodeRecordList[i];
            }
        }
        
        return smallest;
    }

    // Returns true if a node is contained in the list - O(1)
    public bool Contains(GameObject node)
    {
        return node != null && nodeRecordDict.ContainsKey(node);
    }

    // Returns a node record for a node if it is contained in the list - O(1)
    public NodeRecord Find(GameObject node)
    {
        if (node != null && nodeRecordDict.TryGetValue(node, out NodeRecord record))
        {
            return record;
        }
        return null;
    }
    
    #endregion
}
