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

    // Just keeps the list of which tail cards exist and which node
    // they belong to. We loop over this to know which tails need an
    // arrow created for them — the node mapping itself isn't used
    // for positioning anymore (the arrow starts from the tail dot
    // directly, not the node box).
    private Dictionary<string, string> tailToNode =
        new Dictionary<string, string>
    {
        { "tail_10", "node_10" },
        { "tail_20", "node_20" }
    };

    public TaskManager.TaskState currentTaskState;

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

    // Creates one arrow shaft and one arrowhead for each tail card.
    // Only runs once per tail — if the arrow already exists, this
    // just skips it, so it's safe to call this every time markers
    // update without creating duplicates.
    void InitializeArrows()
    {
        foreach (var tail in tailToNode.Keys)
        {
            if (!arrows.ContainsKey(tail))
            {
                // Create the arrow shaft from the prefab and hide it
                // until there's actually something to point at.
                GameObject arrow = Instantiate(arrowPrefab);
                arrow.SetActive(false);
                arrows[tail] = arrow;

                // Create the arrowhead as a small cylinder. Unity's
                // built-in primitives don't include a real cone, so
                // a thin cylinder is used to approximate the look of
                // an arrowhead instead.
                GameObject arrowHead =
                    GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                arrowHead.transform.localScale =
                    new Vector3(0.004f, 0.004f, 0.004f);
                arrowHead.GetComponent<Renderer>().material =
                    new Material(Shader.Find("Universal Render Pipeline/Lit"));
                arrowHead.GetComponent<Renderer>().material.color = Color.white;
                Destroy(arrowHead.GetComponent<Collider>()); // purely visual, no physics needed
                arrowHead.SetActive(false);
                arrowHeads[tail] = arrowHead;
            }
        }
    }

    // Runs every frame. For each tail card, checks if there's a head
    // card close enough to count as "connected", and shows, updates,
    // or hides that tail's arrow accordingly.
    void Update()
    {
        if (spawnedTails == null || spawnedHeads == null) return;

        foreach (var tailEntry in tailToNode)
        {
            string tailName = tailEntry.Key;

            // If the tail card itself isn't visible right now, there's
            // nothing to draw an arrow from — hide it and move on.
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

                // Arrow starts right at the tail card's dot, not the
                // node box, since the tail dot is what the student is
                // actually moving around.
                Vector3 startPosition = tailPosition;

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

    // Finds the closest head card to a given tail card position.
    // Skips any head card that belongs to the same node number as the
    // tail, since a node can't point to itself. Returns null if
    // nothing is within the connection threshold.
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

    // Decides if a tail-to-head connection is the "correct" one,
    // but what counts as correct depends on what stage of the
    // activity we're in. While the student is still building the
    // starting list, only the original 10->20 and 20->30 connections
    // should show green. Once the pointer has been redirected, only
    // 10->30 should show green — everything else, including the old
    // 10->20 connection, should now read as incorrect.
    internal bool IsCorrectConnection(string tailName, string headName)
    {
        // A node can never point to its own incoming connection
        string tailNumber = tailName.Split('_')[1];
        string headNumber = headName.Split('_')[1];
        if (tailNumber == headNumber) return false;

        if (currentTaskState == TaskManager.TaskState.BuildingStartList ||
            currentTaskState == TaskManager.TaskState.ShowingStartList)
        {
            // Still building or confirming the starting list —
            // only the original connections count as correct.
            Dictionary<string, string> correctBefore = new Dictionary<string, string>
        {
            { "tail_10", "head_20" },
            { "tail_20", "head_30" }
        };
            if (!correctBefore.ContainsKey(tailName)) return false;
            return correctBefore[tailName] == headName;
        }
        else
        {
            // Past the redirect step — the only correct connection
            // left is the updated pointer, tail_10 to head_30.
            return tailName == "tail_10" && headName == "head_30";
        }
    }
    // Hides both the arrow shaft and arrowhead for a given tail card
    void HideArrow(string tailName)
    {
        if (arrows.ContainsKey(tailName))
            arrows[tailName].SetActive(false);
        if (arrowHeads.ContainsKey(tailName))
            arrowHeads[tailName].SetActive(false);
    }

    // Positions, stretches, rotates and colors the arrow between two
    // points. The arrow shaft fills the space between the two cards,
    // and the arrowhead sits right at the destination end.
    void UpdateArrow(GameObject arrow, GameObject arrowHead,
        Vector3 from, Vector3 to, bool isCorrect)
    {
        float nodeHalfWidth = 0.025f;
        Vector3 direction = (to - from).normalized;

        // Pull the start and end points in slightly so the arrow
        // doesn't visually overlap with the cards at either end.
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