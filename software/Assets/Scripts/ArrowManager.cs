using System.Collections.Generic;
using UnityEngine;

// This script is responsible for drawing arrows between tail and head cards.
// When a tail card is placed close enough to a head card, an arrow appears
// connecting them. The arrow is green if the connection is correct for the
// linked list, and red if it is wrong.
public class ArrowManager : MonoBehaviour
{
    // The cylinder prefab used as the arrow shaft
    [SerializeField] GameObject arrowPrefab;

    // Reference to TaskManager to check current task state for color feedback
    [SerializeField] TaskManager taskManager;

    // Latest positions of all detected marker cards
    private Dictionary<string, GameObject> spawnedNodes;
    private Dictionary<string, GameObject> spawnedTails;
    private Dictionary<string, GameObject> spawnedHeads;

    // One arrow shaft and one arrowhead per tail card
    private Dictionary<string, GameObject> arrows =
        new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> arrowHeads =
        new Dictionary<string, GameObject>();

    // How close a tail and head card need to be to connect (in meters)
    // 0.15f = 15cm — close enough to be intentional but not accidental
    private float connectionThreshold = 0.15f;

    // Maps each tail card to the node it belongs to
    // Used to start the arrow from the node box rather than the tail dot
    private Dictionary<string, string> tailToNode =
        new Dictionary<string, string>
    {
        { "tail_10", "node_10" },
        { "tail_15", "node_15" },
        { "tail_20", "node_20" },
        { "tail_30", "node_30" }
    };

    // Called by TrackedImageInfo whenever any marker card changes
    // Updates the local card positions and creates arrow objects if needed
    public void UpdateMarkers(
        Dictionary<string, GameObject> nodes,
        Dictionary<string, GameObject> tails,
        Dictionary<string, GameObject> heads)
    {
        spawnedNodes = nodes;
        spawnedTails = tails;
        spawnedHeads = heads;
        InitializeArrows();
    }

    // Creates one arrow shaft and one arrowhead for each tail card
    // Only creates them once — skips if they already exist
    void InitializeArrows()
    {
        foreach (var tail in tailToNode.Keys)
        {
            if (!arrows.ContainsKey(tail))
            {
                // Create the arrow shaft from the prefab and hide it until needed
                GameObject arrow = Instantiate(arrowPrefab);
                arrow.SetActive(false);
                arrows[tail] = arrow;

                // Create the arrowhead as a small cylinder
                // A true cone shape is not available in Unity primitives
                GameObject arrowHead =
                    GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                arrowHead.transform.localScale =
                    new Vector3(0.005f, 0.01f, 0.005f);
                arrowHead.GetComponent<Renderer>().material =
                    new Material(Shader.Find("Universal Render Pipeline/Lit"));
                arrowHead.GetComponent<Renderer>().material.color = Color.white;
                Destroy(arrowHead.GetComponent<Collider>()); // purely visual
                arrowHead.SetActive(false);
                arrowHeads[tail] = arrowHead;
            }
        }
    }

    // Runs every frame — checks each tail card and decides whether to
    // show, update, or hide its arrow based on nearby head cards
    void Update()
    {
        if (spawnedTails == null || spawnedHeads == null) return;

        foreach (var tailEntry in tailToNode)
        {
            string tailName = tailEntry.Key;
            string nodeName = tailEntry.Value;

            // If the tail card is not visible hide its arrow and move on
            bool tailVisible = spawnedTails.ContainsKey(tailName) &&
                               spawnedTails[tailName].activeSelf;
            if (!tailVisible)
            {
                HideArrow(tailName);
                continue;
            }

            Vector3 tailPosition = spawnedTails[tailName].transform.position;

            // Look for the nearest head card within the connection threshold
            string nearestHead = FindNearestHead(tailPosition, tailName);

            if (nearestHead != null)
            {
                Vector3 headPosition = spawnedHeads[nearestHead].transform.position;

                // Start the arrow from the node box if visible
                // otherwise fall back to starting from the tail dot
                Vector3 startPosition = tailPosition;
                if (spawnedNodes.ContainsKey(nodeName) &&
                    spawnedNodes[nodeName].activeSelf)
                {
                    startPosition = spawnedNodes[nodeName].transform.position;
                }

                // Check if this connection follows the correct linked list order
                bool isCorrect = IsCorrectConnection(tailName, nearestHead);

                // Show the arrow and update its position and color
                arrows[tailName].SetActive(true);
                arrowHeads[tailName].SetActive(true);
                UpdateArrow(
                    arrows[tailName],
                    arrowHeads[tailName],
                    startPosition,
                    headPosition,
                    isCorrect
                );
            }
            else
            {
                // No head card close enough — hide the arrow
                HideArrow(tailName);
            }
        }
    }

    // Finds the closest head card to a given tail card position
    // Skips head cards that belong to the same node as the tail
    // Returns null if nothing is within the connection threshold
    string FindNearestHead(Vector3 tailPosition, string tailName)
    {
        string nearestHead = null;
        float nearestDistance = connectionThreshold;

        // Extract node number from tail name e.g. "tail_15" gives "15"
        string tailNumber = tailName.Split('_')[1];

        foreach (var headEntry in spawnedHeads)
        {
            if (!headEntry.Value.activeSelf) continue;

            // Skip head_15 when processing tail_15 — a node cannot point to itself
            string headNumber = headEntry.Key.Split('_')[1];
            if (headNumber == tailNumber) continue;

            float distance = Vector3.Distance(
                tailPosition, headEntry.Value.transform.position);

            // Keep track of the closest one found so far
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestHead = headEntry.Key;
            }
        }
        return nearestHead;
    }

    // Returns true if a tail to head connection follows the correct linked list order
    // tail_15 can correctly connect to head_20 or head_30 since both are valid states
    bool IsCorrectConnection(string tailName, string headName)
    {
        // A node can never point to its own incoming connection
        string tailNumber = tailName.Split('_')[1];
        string headNumber = headName.Split('_')[1];
        if (tailNumber == headNumber) return false;

        // All valid connections based on the linked list 10 -> 15 -> 20 -> 30 -> 45
        // tail_15 has two valid targets because it points to head_30 before insertion
        // and head_20 after insertion — both are correct at different stages
        Dictionary<string, List<string>> validConnections =
            new Dictionary<string, List<string>>
        {
            { "tail_10", new List<string> { "head_15" } },
            { "tail_15", new List<string> { "head_20", "head_30" } },
            { "tail_20", new List<string> { "head_30" } },
            { "tail_30", new List<string> { "head_45" } }
        };

        if (!validConnections.ContainsKey(tailName)) return false;
        return validConnections[tailName].Contains(headName);
    }

    // Hides both the arrow shaft and arrowhead for a given tail card
    void HideArrow(string tailName)
    {
        if (arrows.ContainsKey(tailName))
            arrows[tailName].SetActive(false);
        if (arrowHeads.ContainsKey(tailName))
            arrowHeads[tailName].SetActive(false);
    }

    // Positions, stretches, rotates and colors the arrow between two points
    // The arrow starts at the edge of the from node and ends at the edge of the to node
    void UpdateArrow(GameObject arrow, GameObject arrowHead,
        Vector3 from, Vector3 to, bool isCorrect)
    {
        float nodeHalfWidth = 0.025f;
        Vector3 direction = (to - from).normalized;

        // Offset start and end points to the edges of the nodes
        // so the arrow does not overlap with the node boxes themselves
        Vector3 startPoint = from + direction * nodeHalfWidth;
        Vector3 endPoint = to - direction * nodeHalfWidth;

        // Position the shaft at the midpoint and stretch it to fill the gap
        arrow.transform.position = (startPoint + endPoint) / 2f;
        float distance = Vector3.Distance(startPoint, endPoint);
        // Divided by 2 because Unity cylinders are 2 units tall by default
        arrow.transform.localScale =
            new Vector3(0.001f, distance / 2f, 0.001f);
        arrow.transform.up = direction;

        // Place the arrowhead at the destination end
        arrowHead.transform.position = endPoint;
        arrowHead.transform.up = direction;

        // Green arrow = correct connection, Red arrow = wrong connection
        Color arrowColor = isCorrect ? Color.green : Color.red;

        var arrowRenderer = arrow.GetComponent<Renderer>();
        if (arrowRenderer != null)
        {
            arrowRenderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            arrowRenderer.material.color = arrowColor;
        }

        var headRenderer = arrowHead.GetComponent<Renderer>();
        if (headRenderer != null)
        {
            headRenderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            headRenderer.material.color = arrowColor;
        }
    }
}