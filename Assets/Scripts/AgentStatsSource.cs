using System;
using UnityEngine;

public class AgentStatsSource : MonoBehaviour
{
    [Header("Agent Info")]
    public string agentName = "Agent";
    public int packageCount = 0;
    public string deliveryStatus = "In Progress";

    [Header("Auto Naming")]
    public bool autoNameFromGameObject = true;
    public bool useRootName = true; // if script is on a child collider, still use the car root name

    [Header("Refs (Optional)")]
    public Rigidbody rb;

    [Header("Live Values")]
    public float speedMS;
    public float totalDistanceM;

    [Header("Speed Settings")]
    public bool usePositionDeltaFallback = true;
    public float minSpeedThreshold = 0.02f;
    public float smoothing = 8f;

    // Collision info (shown in UI)
    public static string lastCollisionMessage = "No Collision";
    private static float lastCollisionTime;

    private Vector3 lastPos;

    void Reset()
    {
        ApplyAutoName(force: true);
    }

    void OnValidate()
    {
        ApplyAutoName(force: false);
    }

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        ApplyAutoName(force: false);
        lastPos = transform.position;
    }

    void OnEnable()
    {
        lastPos = transform.position;
    }

    void ApplyAutoName(bool force)
    {
        if (!autoNameFromGameObject) return;

        bool shouldAutoSet =
            force ||
            string.IsNullOrWhiteSpace(agentName) ||
            agentName.Trim().Equals("Agent", StringComparison.OrdinalIgnoreCase);

        if (!shouldAutoSet) return;

        agentName = useRootName ? transform.root.name : gameObject.name;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 currentPos = transform.position;

        // Position-based speed + distance
        float frameDistance = Vector3.Distance(currentPos, lastPos);
        float posSpeed = frameDistance / dt;

        // Rigidbody-based speed (if meaningful)
        float rbSpeed = 0f;
        bool rbUsable = (rb != null && !rb.isKinematic);
        if (rbUsable) rbSpeed = rb.velocity.magnitude;

        // Choose speed source
        float measuredSpeed;
        if (rbUsable && rbSpeed > minSpeedThreshold)
            measuredSpeed = rbSpeed;
        else if (usePositionDeltaFallback)
            measuredSpeed = posSpeed;
        else
            measuredSpeed = rbSpeed;

        if (measuredSpeed < minSpeedThreshold) measuredSpeed = 0f;

        // Smooth
        if (smoothing <= 0f)
        {
            speedMS = measuredSpeed;
        }
        else
        {
            float t = 1f - Mathf.Exp(-smoothing * dt);
            speedMS = Mathf.Lerp(speedMS, measuredSpeed, t);
        }

        totalDistanceM += frameDistance;
        lastPos = currentPos;

        // Clear collision after 3 seconds
        if (Time.time - lastCollisionTime > 3f)
            lastCollisionMessage = "No Collision";
    }

    void OnCollisionEnter(Collision c)
    {
        // Only report collisions with another agent (another object that has AgentStatsSource)
        var otherAgent = c.collider.GetComponentInParent<AgentStatsSource>();
        if (otherAgent == null || otherAgent == this) return;

        string otherName = string.IsNullOrWhiteSpace(otherAgent.agentName) ? otherAgent.transform.root.name : otherAgent.agentName;
        lastCollisionMessage = $"{agentName} collided with {otherName}";
        lastCollisionTime = Time.time;
    }

    // If you use triggers instead of collisions
    void OnTriggerEnter(Collider other)
    {
        var otherAgent = other.GetComponentInParent<AgentStatsSource>();
        if (otherAgent == null || otherAgent == this) return;

        string otherName = string.IsNullOrWhiteSpace(otherAgent.agentName) ? otherAgent.transform.root.name : otherAgent.agentName;
        lastCollisionMessage = $"{agentName} touched {otherName}";
        lastCollisionTime = Time.time;
    }
}
