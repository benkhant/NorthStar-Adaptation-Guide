using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using UnityEngine;
using BEERLabs.ProjectEsky;
using BEERLabs.ProjectEsky.Extras.Modules;

// Adapted from Prof. Lee's AROpenCVMarkerTracking.cs.
// Swaps AR Foundation's camera for Esky's EskyRGBSensorModule as the image source.
public class EskyAROpenCVMarkerTracking : MonoBehaviour
{
    [Header("Esky camera source")]
    [SerializeField] private SensorImageSource rgbSensorSource;
    [SerializeField] private Transform arCameraTransform; // e.g. ARCameraRig/Head

    [Header("Target object")]
    [SerializeField] private GameObject targetModel;

    [System.Serializable]
    public class MarkerPrefabMapping
    {
        public int markerId;
        public string cardName;
        public GameObject prefab;
    }

    [Header("Marker to Prefab Mapping")]
    [SerializeField] private List<MarkerPrefabMapping> markerPrefabs = new List<MarkerPrefabMapping>();

    [Header("Task Manager")]
    [SerializeField] private TaskManager taskManager;
    [SerializeField] private ArrowManager arrowManager;

    private Dictionary<int, GameObject> spawnedMarkers = new Dictionary<int, GameObject>();
    private Dictionary<int, GameObject> prefabLookup;

    private Dictionary<int, int> missedFrames = new Dictionary<int, int>();
    private const int maxMissedFrames = 10;

    [Header("Debug Display")]
    [SerializeField] private UnityEngine.UI.RawImage cameraPreviewImage;

    private Mat fisheyeDistCoeffs;
    private Mat newCamMatrix;
    private int latestChannels;

    public float markerSize = 0.05f;
    public float gridSize = 0.045f;
    public float marginSize = 0.009f;
    [SerializeField] private bool showDebugInfo = true;

    private OpenCvSharp.Aruco.Dictionary dictionary;
    private DetectorParameters parameters;
    private Mat camMatrix;
    private Mat distCoeffs;
    private bool intrinsicsReady = false;

    private readonly object frameLock = new object();
    private byte[] latestFrameBuffer;
    private int latestWidth;
    private int latestHeight;
    private bool hasNewFrame = false;

    private readonly Dictionary<int, Vector3> boardMarkerPositions = new Dictionary<int, Vector3>
    {
        { 0, new Vector3(1,  0, 0) },
        { 1, new Vector3(3,  0, 0) },
        { 2, new Vector3(0, -1, 0) },
        { 3, new Vector3(2, -1, 0) },
        { 4, new Vector3(4, -1, 0) },
        { 5, new Vector3(1, -2, 0) },
        { 6, new Vector3(3, -2, 0) },
        { 7, new Vector3(0, -3, 0) },
        { 8, new Vector3(2, -3, 0) },
        { 9, new Vector3(4, -3, 0) }
    };

    private void Start()
    {
        if (!rgbSensorSource) { Debug.LogError("[EskyCvAruco] rgbSensorSource is required."); enabled = false; return; }
        if (!arCameraTransform) { Debug.LogError("[EskyCvAruco] arCameraTransform is required."); enabled = false; return; }

        dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
        //dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict6X6_250);
        parameters = new DetectorParameters
        {
            CornerRefinementMethod = CornerRefineMethod.Subpix
        };

        BuildCameraIntrinsics();
        rgbSensorSource.SubscribeImageCallback(OnImageReceived);
    }

    private void BuildPrefabLookup()
    {
        prefabLookup = new Dictionary<int, GameObject>();
        foreach (var m in markerPrefabs)
            prefabLookup[m.markerId] = m.prefab;
    }

    private void OnDestroy()
    {
        camMatrix?.Dispose();
        distCoeffs?.Dispose();
        newCamMatrix?.Dispose();
    }

    private void BuildCameraIntrinsics()
    {
        var cal = rgbSensorSource.myCalibrations;
        if (cal == null) { Debug.LogError("[EskyCvAruco] myCalibrations is null."); return; }

        camMatrix = new Mat(3, 3, MatType.CV_64FC1);
        camMatrix.Set(0, 0, (double)cal.fx / 2.0);
        camMatrix.Set(0, 1, 0.0);
        camMatrix.Set(0, 2, (double)cal.cx / 2.0);
        camMatrix.Set(1, 0, 0.0);
        camMatrix.Set(1, 1, (double)cal.fy / 2.0);
        camMatrix.Set(1, 2, (double)cal.cy / 2.0);
        camMatrix.Set(2, 0, 0.0);
        camMatrix.Set(2, 1, 0.0);
        camMatrix.Set(2, 2, 1.0);

        distCoeffs = new Mat(1, 5, MatType.CV_64FC1);
        distCoeffs.Set(0, 0, (double)cal.d1);
        distCoeffs.Set(0, 1, (double)cal.d2);
        distCoeffs.Set(0, 2, (double)cal.d3);
        distCoeffs.Set(0, 3, (double)cal.d4);
        distCoeffs.Set(0, 4, 0.0);

        fisheyeDistCoeffs = new Mat(4, 1, MatType.CV_64FC1);
        fisheyeDistCoeffs.Set(0, 0, (double)cal.d1);
        fisheyeDistCoeffs.Set(1, 0, (double)cal.d2);
        fisheyeDistCoeffs.Set(2, 0, (double)cal.d3);
        fisheyeDistCoeffs.Set(3, 0, (double)cal.d4);
        
        intrinsicsReady = true;
    }

    private Pose GetActiveCameraPose() => new Pose(arCameraTransform.position, arCameraTransform.rotation);

    private void OnImageReceived(ImageData d)
    {
        if (d.info == IntPtr.Zero || d.width <= 0 || d.height <= 0) return;
        lock (frameLock)
        {
            if (latestFrameBuffer == null || latestFrameBuffer.Length != d.lengthOfArray)
                latestFrameBuffer = new byte[d.lengthOfArray];
            Marshal.Copy(d.info, latestFrameBuffer, 0, d.lengthOfArray);

            latestChannels = d.pixelCount > 0 ? d.pixelCount : 1;

            // Reported width/height may not match the actual buffer if the
            // native library reports a different resolution than it sends.
            // Solve for the real scale factor from total data size instead
            // of assuming a fixed ratio.
            double reportedPixels = (double)d.width * d.height;
            double actualPixels = (double)d.lengthOfArray / latestChannels;
            double scale = Math.Sqrt(reportedPixels / actualPixels);

            latestWidth = Math.Max(1, (int)Math.Round(d.width / scale));
            latestHeight = Math.Max(1, (int)Math.Round(d.height / scale));

            hasNewFrame = true;
        }
    }

    private Texture2D readTexture;

    private Mat GetCurrentFrameMat()
    {
        if (cameraPreviewImage == null || cameraPreviewImage.texture == null) return null;

        var renderTex = cameraPreviewImage.texture as RenderTexture;
        if (renderTex == null) return null;

        if (readTexture == null || readTexture.width != renderTex.width || readTexture.height != renderTex.height)
            readTexture = new Texture2D(renderTex.width, renderTex.height, TextureFormat.RGBA32, false);

        var prevActive = RenderTexture.active;
        RenderTexture.active = renderTex;
        readTexture.ReadPixels(new UnityEngine.Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
        readTexture.Apply();
        RenderTexture.active = prevActive;

        var raw = readTexture.GetRawTextureData();
        var bgra = new Mat(readTexture.height, readTexture.width, MatType.CV_8UC4);
        Marshal.Copy(raw, 0, bgra.Data, raw.Length);
        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.RGBA2BGR);
        bgra.Dispose();
        return bgr;
    }

    private void Update()
    {
        if (!intrinsicsReady) return;
        UpdateArMarkers();
    }

    private void UpdateArMarkers()
    {
        var img = GetCurrentFrameMat();
        if (img == null || img.Empty()) return;
        if (dictionary == null) { img.Dispose(); return; }

        CvAruco.DetectMarkers(img, dictionary, out var corners, out var ids, parameters, out var rejected);
        foreach (var id in ids) Debug.Log("[EskyCvAruco] Saw marker ID: " + id);
        Debug.Log("[EskyCvAruco] Detected: " + ids.Length + " markers, Rejected candidates: " + rejected.Length);

        if (prefabLookup == null) BuildPrefabLookup();

        var seenThisFrame = new HashSet<int>();

        for (int i = 0; i < ids.Length; i++)
        {
            int id = ids[i];
            seenThisFrame.Add(id);

            MarkerPrefabMapping m = markerPrefabs.Find(mp => mp.markerId == id);
            if (m == null) continue;

            bool isTail = m.cardName != null && m.cardName.StartsWith("tail_");
            bool isHead = m.cardName != null && m.cardName.StartsWith("head_");

            if (!isTail && !isHead && m.prefab == null) continue;

            var rvecMat = new Mat();
            var tvecMat = new Mat();
            CvAruco.EstimatePoseSingleMarkers(new[] { corners[i] }, markerSize, camMatrix, distCoeffs, rvecMat, tvecMat);

            var rvec = new Vec3d(rvecMat.Get<double>(0, 0), rvecMat.Get<double>(0, 1), rvecMat.Get<double>(0, 2));
            var tvec = new Vec3d(tvecMat.Get<double>(0, 0), tvecMat.Get<double>(0, 1), tvecMat.Get<double>(0, 2));
            rvecMat.Dispose();
            tvecMat.Dispose();

            var rvecForRodrigues = new Mat(1, 3, MatType.CV_64FC1);
            rvecForRodrigues.Set(0, 0, rvec.Item0);
            rvecForRodrigues.Set(0, 1, rvec.Item1);
            rvecForRodrigues.Set(0, 2, rvec.Item2);

            var rotMat = new Mat();
            Cv2.Rodrigues(rvecForRodrigues, rotMat);
            rvecForRodrigues.Dispose();

            var markerInCamera = new Matrix4x4();
            markerInCamera.SetColumn(0, new Vector4((float)rotMat.Get<double>(0, 0), (float)rotMat.Get<double>(1, 0), (float)rotMat.Get<double>(2, 0), 0));
            markerInCamera.SetColumn(1, new Vector4((float)rotMat.Get<double>(0, 1), (float)rotMat.Get<double>(1, 1), (float)rotMat.Get<double>(2, 1), 0));
            markerInCamera.SetColumn(2, new Vector4((float)rotMat.Get<double>(0, 2), (float)rotMat.Get<double>(1, 2), (float)rotMat.Get<double>(2, 2), 0));
            markerInCamera.SetColumn(3, new Vector4((float)tvec.Item0, (float)tvec.Item1, (float)tvec.Item2, 1));
            rotMat.Dispose();

            var flipY = Matrix4x4.Scale(new Vector3(1, -1, 1));
            markerInCamera = flipY * markerInCamera * flipY;

            var cameraPose = GetActiveCameraPose();
            var cameraInWorld = Matrix4x4.TRS(cameraPose.position, cameraPose.rotation, Vector3.one);
            var markerInWorld = cameraInWorld * markerInCamera;

            if (!spawnedMarkers.TryGetValue(id, out var obj) || obj == null)
            {
                if (isTail) obj = CreateDot(new UnityEngine.Color(1f, 0.5f, 0f));
                else if (isHead) obj = CreateDot(new UnityEngine.Color(0.5f, 0f, 0.5f));
                else obj = Instantiate(m.prefab);
                spawnedMarkers[id] = obj;
            }
            obj.transform.SetPositionAndRotation(markerInWorld.GetColumn(3), markerInWorld.rotation);
            obj.SetActive(true);
        }

        foreach (var kvp in spawnedMarkers)
        {
            if (seenThisFrame.Contains(kvp.Key))
            {
                missedFrames[kvp.Key] = 0;
            } else if (kvp.Value != null) {
                if (!missedFrames.ContainsKey(kvp.Key)) missedFrames[kvp.Key] = 0;
                missedFrames[kvp.Key]++;
                if (missedFrames[kvp.Key] > maxMissedFrames)
                {
                    kvp.Value.SetActive(false);
                }
            }
        }

        if (taskManager != null)
        {
            var nodeDict = new Dictionary<string, GameObject>();
            var tailDict = new Dictionary<string, GameObject>();
            var headDict = new Dictionary<string, GameObject>();

            foreach (var m in markerPrefabs)
            {
                if (string.IsNullOrEmpty(m.cardName)) continue;
                if (!spawnedMarkers.TryGetValue(m.markerId, out var obj) || obj == null) continue;

                if (m.cardName.StartsWith("node_")) nodeDict[m.cardName] = obj;
                else if (m.cardName.StartsWith("tail_")) tailDict[m.cardName] = obj;
                else if (m.cardName.StartsWith("head_")) headDict[m.cardName] = obj;
            }
            taskManager.UpdateMarkers(nodeDict,  tailDict, headDict);
            if (arrowManager != null) arrowManager.UpdateMarkers(nodeDict, tailDict, headDict);
        }
        img.Dispose();
    }
    private GameObject CreateDot(UnityEngine.Color color)
    {
        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
        var renderer = dot.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        renderer.material.color = color;
        Destroy(dot.GetComponent<Collider>());
        return dot;
    }
}