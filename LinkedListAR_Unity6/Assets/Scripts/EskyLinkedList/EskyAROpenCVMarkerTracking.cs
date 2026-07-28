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
        Debug.Log("[EskyCvAruco] Detected: " + ids.Length + " markers, Rejected candidates: " + rejected.Length);

        if (ids.Length > 0)
        {
            if (showDebugInfo) CvAruco.DrawDetectedMarkers(img, corners, ids);

            var boardPoints = new List<Point3f>();
            var imagePoints = new List<Point2f>();
            const int boardCols = 5, boardRows = 4;
            var boardWidth = boardCols * gridSize;
            var boardHeight = boardRows * gridSize;
            var boardCenterX = boardWidth * 0.5f;
            var boardCenterY = -boardHeight * 0.5f;

            for (var i = 0; i < ids.Length; i++)
            {
                var markerId = ids[i];
                if (!boardMarkerPositions.TryGetValue(markerId, out var boardOrigin)) continue;

                var x0 = boardOrigin.x * gridSize + marginSize;
                var y0 = boardOrigin.y * gridSize - marginSize;
                x0 -= boardCenterX; y0 -= boardCenterY;

                boardPoints.Add(new Point3f(x0, y0, 0));
                boardPoints.Add(new Point3f(x0 + markerSize, y0, 0));
                boardPoints.Add(new Point3f(x0 + markerSize, y0 - markerSize, 0));
                boardPoints.Add(new Point3f(x0, y0 - markerSize, 0));

                var dc = corners[i];
                imagePoints.Add(dc[0]); imagePoints.Add(dc[1]); imagePoints.Add(dc[2]); imagePoints.Add(dc[3]);
            }

            if (boardPoints.Count >= 4)
            {
                var rvec = new Mat(); var tvec = new Mat();
                try
                {
                    Cv2.SolvePnP(InputArray.Create(boardPoints), InputArray.Create(imagePoints), camMatrix, distCoeffs, rvec, tvec);

                    var rotMat = new Mat();
                    Cv2.Rodrigues(rvec, rotMat);

                    var boardInCamera = new Matrix4x4();
                    boardInCamera.SetColumn(0, new Vector4((float)rotMat.Get<double>(0, 0), (float)rotMat.Get<double>(1, 0), (float)rotMat.Get<double>(2, 0), 0));
                    boardInCamera.SetColumn(1, new Vector4((float)rotMat.Get<double>(0, 1), (float)rotMat.Get<double>(1, 1), (float)rotMat.Get<double>(2, 1), 0));
                    boardInCamera.SetColumn(2, new Vector4((float)rotMat.Get<double>(0, 2), (float)rotMat.Get<double>(1, 2), (float)rotMat.Get<double>(2, 2), 0));
                    boardInCamera.SetColumn(3, new Vector4((float)tvec.Get<double>(0), (float)tvec.Get<double>(1), (float)tvec.Get<double>(2), 1));
                    rotMat.Dispose();

                    var flipY = Matrix4x4.Scale(new Vector3(1, -1, 1));
                    boardInCamera = flipY * boardInCamera * flipY;

                    var cameraPose = GetActiveCameraPose();
                    var cameraInWorld = Matrix4x4.TRS(cameraPose.position, cameraPose.rotation, Vector3.one);
                    var boardInWorld = cameraInWorld * boardInCamera;

                    var zOffset = 0.05f;
                    Vector3 boardPos = boardInWorld.GetColumn(3);
                    Vector3 boardBack = -(Vector3)boardInWorld.GetColumn(2);
                    Vector3 targetPos = boardPos + boardBack * zOffset;

                    if (targetModel)
                    {
                        targetModel.transform.SetPositionAndRotation(targetPos, boardInWorld.rotation);
                        targetModel.transform.localScale = Vector3.one * markerSize;
                        targetModel.SetActive(true);
                    }
                }
                catch (OpenCvSharp.OpenCVException e)
                {
                    Debug.LogWarning("[EskyCvAruco] solvePnP failed: " + e.Message);
                }
                rvec.Dispose(); tvec.Dispose();
            }
        }
        else
        {
            if (showDebugInfo) CvAruco.DrawDetectedMarkers(img, rejected, null, new Scalar(255, 0, 0));
            if (targetModel) targetModel.SetActive(false);
        }
        img.Dispose();
    }
}