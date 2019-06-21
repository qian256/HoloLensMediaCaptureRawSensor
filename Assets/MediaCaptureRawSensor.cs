using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
#if !UNITY_EDITOR && UNITY_METRO
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using System.Threading.Tasks;
using System;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using System.Threading;
using System.Linq;
using System.Collections.Concurrent;
#endif

public class MediaCaptureRawSensor : MonoBehaviour {

    public static string TAG = "MediaCaptureRawSensor";

    public int id;
    public Material mediaMaterial;
    private Texture2D mediaTexture;
    
    private enum CaptureStatus {
        Clean,
        Initialized,
        Running
    }
    private CaptureStatus captureStatus = CaptureStatus.Clean;
            

#if !UNITY_EDITOR && UNITY_METRO
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    private SoftwareBitmap upBitmap = null;
    private MediaCapture mediaCapture;

    private MediaFrameReader frameReader = null;

    private int videoWidth = 0;
    private int videoHeight = 0;


    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private async Task<bool> InitializeMediaCaptureAsync() {
        if (captureStatus != CaptureStatus.Clean) {
            Debug.Log(TAG + " " + id + ": InitializeMediaCaptureAsync() fails because of incorrect status");
            return false;
        }

        if (mediaCapture != null) {
            return false;
        }

        var allGroups = await MediaFrameSourceGroup.FindAllAsync();
        foreach (var group in allGroups) {
            Debug.Log(group.DisplayName + ", " + group.Id);
        }
        
        if (allGroups.Count <= 0) {
            Debug.Log(TAG + " " + id + ": InitializeMediaCaptureAsync() fails because there is no MediaFrameSourceGroup");
            return false;
        }
        
        // Initialize mediacapture with the source group.
        mediaCapture = new MediaCapture();
        var settings = new MediaCaptureInitializationSettings {
            SourceGroup = allGroups[0],
            // This media capture can share streaming with other apps.
            SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            // Only stream video and don't initialize audio capture devices.
            StreamingCaptureMode = StreamingCaptureMode.Video,
            // Set to CPU to ensure frames always contain CPU SoftwareBitmap images
            // instead of preferring GPU D3DSurface images.
            MemoryPreference = MediaCaptureMemoryPreference.Cpu
        };

        await mediaCapture.InitializeAsync(settings);
        Debug.Log(TAG + " " + id + ": MediaCapture is successfully initialized in shared mode.");

        // logging all frame source information
        string logString = "";
        foreach (var frameSource in mediaCapture.FrameSources) {
            var info = frameSource.Value.Info;
            logString += info.Id + ", " + info.MediaStreamType + ", " + info.SourceKind + "\n";
            logString += "Total number of SupportedFormats is " + frameSource.Value.SupportedFormats.Count + "\n";
            foreach (var format in frameSource.Value.SupportedFormats) {
                logString += format.VideoFormat.Width + " x " + format.VideoFormat.Height + ", Major type: " + format.MajorType + ", Subtype: " + format.Subtype +
                        ", Framerate: " + format.FrameRate.Numerator + "/" + format.FrameRate.Denominator + "\n";
            }
        }
        Debug.Log(logString);
        MediaFrameSource targetFrameSource = mediaCapture.FrameSources.Values.ElementAt(id);
        MediaFrameFormat targetResFormat = targetFrameSource.SupportedFormats[0];
        try {
            // choose the smallest resolution
            //var targetResFormat = mediaFrameSourceVideoPreview.SupportedFormats.OrderBy(x => x.VideoFormat.Width * x.VideoFormat.Height).FirstOrDefault();
            // choose the specific resolution
            //var targetResFormat = mediaFrameSourceVideoPreview.SupportedFormats.OrderBy(x => (x.VideoFormat.Width == 1344 && x.VideoFormat.Height == 756)).FirstOrDefault();
            await targetFrameSource.SetFormatAsync(targetResFormat);
            frameReader = await mediaCapture.CreateFrameReaderAsync(targetFrameSource, targetResFormat.Subtype);
            frameReader.FrameArrived += OnFrameArrived;
            videoWidth = Convert.ToInt32(targetResFormat.VideoFormat.Width);
            videoHeight = Convert.ToInt32(targetResFormat.VideoFormat.Height);
            Debug.Log(TAG + " " + id + ": FrameReader is successfully initialized, " + videoWidth + "x" + videoHeight + 
                ", Framerate: " + targetResFormat.FrameRate.Numerator + "/" + targetResFormat.FrameRate.Denominator + 
                ", Major type: " + targetResFormat.MajorType + ", Subtype: " + targetResFormat.Subtype);
        }
        catch (Exception e) {
            Debug.Log(TAG + " " + id + ": FrameReader is not initialized");
            Debug.Log(TAG + " " + id + ": Exception: " + e);
            return false;
        }
        
        captureStatus = CaptureStatus.Initialized;
        return true;
    }
    
    private async Task<bool> StartFrameReaderAsync() {
        Debug.Log(TAG + " " + id + " StartFrameReaderAsync() thread ID is " + Thread.CurrentThread.ManagedThreadId);
        if (captureStatus != CaptureStatus.Initialized) {
            Debug.Log(TAG + ": StartFrameReaderAsync() fails because of incorrect status");
            return false;
        }
        
        MediaFrameReaderStartStatus status = await frameReader.StartAsync();
        if (status == MediaFrameReaderStartStatus.Success) {
            Debug.Log(TAG + " " + id + ": StartFrameReaderAsync() is successful");
            captureStatus = CaptureStatus.Running;
            return true;
        }
        else {
            Debug.Log(TAG + " " + id + ": StartFrameReaderAsync() is successful, status = " + status);
            return false;
        }
    }

    private async Task<bool> StopFrameReaderAsync() {
        if (captureStatus != CaptureStatus.Running) {
            Debug.Log(TAG + " " + id + ": StopFrameReaderAsync() fails because of incorrect status");
            return false;
        }
        await frameReader.StopAsync();
        captureStatus = CaptureStatus.Initialized;
        Debug.Log(TAG + " " + id + ": StopFrameReaderAsync() is successful");
        return true;
    }

    private bool onFrameArrivedProcessing = false;
    
    private unsafe void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args) {
        // TryAcquireLatestFrame will return the latest frame that has not yet been acquired.
        // This can return null if there is no such frame, or if the reader is not in the
        // "Started" state. The latter can occur if a FrameArrived event was in flight
        // when the reader was stopped.
        if (onFrameArrivedProcessing) {
            Debug.Log(TAG + " " + id + " OnFrameArrived() is still processing");
            return;
        }
        onFrameArrivedProcessing = true;
        using (var frame = sender.TryAcquireLatestFrame()) {
            if (frame != null) {

                var softwareBitmap = SoftwareBitmap.Convert(frame.VideoMediaFrame.SoftwareBitmap, BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore);

                //using (var input = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Read))
                //using (var inputReference = input.CreateReference()) {
                //    byte* inputBytes;
                //    uint inputCapacity;
                //    ((IMemoryBufferByteAccess)inputReference).GetBuffer(out inputBytes, out inputCapacity);
                //    downPtr = (IntPtr)inputBytes;
                //    Interlocked.Exchange(ref upPtr, downPtr);
                //}            
                //Debug.Log(TAG + " OnFrameArrived() thread ID is " + Thread.CurrentThread.ManagedThreadId);

                Interlocked.Exchange(ref upBitmap, softwareBitmap);
            }
        }
        onFrameArrivedProcessing = false;
    }
    
    
    async void InitializeMediaCaptureAsyncWrapper() {
        await InitializeMediaCaptureAsync();
    }

    async void StartFrameReaderAsyncWrapper() {
        await StartFrameReaderAsync();
    }

    async void StopFrameReaderAsyncWrapper() {
        await StopFrameReaderAsync();
    }

    private bool textureInitialized = false;

    // Update is called once per frame
    unsafe void Update() {

        if (!textureInitialized && captureStatus == CaptureStatus.Initialized) {
            mediaTexture = new Texture2D(videoWidth, videoHeight, TextureFormat.RGBA32, false);
            mediaMaterial.mainTexture = mediaTexture;
            textureInitialized = true;
        }

        if (upBitmap != null && textureInitialized) {
            using (var input = upBitmap.LockBuffer(BitmapBufferAccessMode.Read))
            using (var inputReference = input.CreateReference()) {
                byte* inputBytes;
                uint inputCapacity;
                ((IMemoryBufferByteAccess)inputReference).GetBuffer(out inputBytes, out inputCapacity);
                mediaTexture.LoadRawTextureData((IntPtr)inputBytes, videoWidth * videoHeight * 4);
                mediaTexture.Apply();
            }
        }
    }


    void Start() {
        captureStatus = CaptureStatus.Clean;
        InitializeMediaCaptureAsyncWrapper();
    }


    void OnApplicationQuit() {
        if (captureStatus == CaptureStatus.Running) {
            var stopTask = StopFrameReaderAsync();
            stopTask.Wait();
        }
    }

    public void OnClick() {
        Debug.Log(TAG + " " + id + " OnClick()");
        if (captureStatus == CaptureStatus.Initialized) {
            StartFrameReaderAsyncWrapper();
        }
        else if (captureStatus == CaptureStatus.Running) {
            StopFrameReaderAsyncWrapper();
        }
    }

#else
    
    public void OnClick() {
        ;
    }

#endif


}
