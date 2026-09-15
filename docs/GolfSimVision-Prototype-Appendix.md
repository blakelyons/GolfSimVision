# GolfSimVision — Original Exploratory Notes & Prototype Appendix

> This file preserves the exploratory technical notes and prototype code from the original kickoff.
> It is **not** the implementation specification. Validate APIs, libraries, performance claims, and assumptions before reuse.

---

Posible prototype code for inspiration:

import SwiftUI
import AVFoundation

struct StreamingControlView: View {
    @StateObject private var cameraEngine = AdaptiveStreamingEngine()
    @State private var selectedConnectionMode: ConnectionMode = .cable
    
    enum ConnectionMode {
        case wifi, cable
    }
    
    var body: some View {
        VStack(spacing: 16) {
            // Header & Status
            VStack(spacing: 4) {
                Text("Golf Simulator WebCam Source")
                    .font(.title3)
                    .bold()
                
                HStack {
                    Circle()
                        .fill(cameraEngine.isStreaming ? Color.green : Color.red)
                        .frame(width: 10, height: 10)
                    Text(cameraEngine.isStreaming ? "Server Active" : "Server Stopped")
                        .font(.caption)
                        .foregroundColor(.secondary)
                }
            }
            .padding(.top, 8)
            
            // Live Camera Alignment Preview Window
            ZStack {
                CameraPreviewContainer(session: cameraEngine.captureSession)
                    .background(Color.black)
                    .cornerRadius(12)
                    .innerShadow(color: Color.black.opacity(0.5), radius: 6, x: 0, y: 3)
                
                // Alignment Crosshair Guide Overlay
                VStack {
                    Spacer()
                    HStack {
                        Spacer()
                        Image(systemName: "plus.circle")
                            .font(.title)
                            .foregroundColor(.white.opacity(0.4))
                        Spacer()
                    }
                    Spacer()
                }
            }
            .frame(height: 200)
            .padding(.horizontal)
            
            // Manual Exposure Control Panel
            VStack(spacing: 12) {
                Text("🎛️ Camera Manual Tuning (Anti-Blur)")
                    .font(.caption)
                    .bold()
                    .foregroundColor(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                
                // Shutter Speed Slider
                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text("Shutter Speed (Duration)")
                        Spacer()
                        Text("1/\(Int(1.0 / cameraEngine.shutterSpeed))s")
                            .monospacedDigit()
                            .bold()
                    }
                    .font(.caption)
                    
                    Slider(value: $cameraEngine.shutterSpeed, in: cameraEngine.minShutterDuration...cameraEngine.maxShutterDuration) { _ in
                        cameraEngine.updateManualExposure()
                    }
                }
                
                // ISO / Sensor Gain Slider
                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text("Sensor Gain (ISO)")
                        Spacer()
                        Text("\(Int(cameraEngine.isoValue))")
                            .monospacedDigit()
                            .bold()
                    }
                    .font(.caption)
                    
                    Slider(value: $cameraEngine.isoValue, in: cameraEngine.minISO...cameraEngine.maxISO) { _ in
                        cameraEngine.updateManualExposure()
                    }
                }
            }
            .padding()
            .background(Color(.secondarySystemBackground))
            .cornerRadius(12)
            .padding(.horizontal)
            
            // Connection Instructions Card
            Picker("Mode", selection: $selectedConnectionMode) {
                Text("🔌 Wired Cable").tag(ConnectionMode.cable)
                Text("📶 Wi-Fi").tag(ConnectionMode.wifi)
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
            
            // Live URL Display Area
            VStack(spacing: 4) {
                Text(cameraEngine.rtspURLString)
                    .font(.system(.footnote, design: .monospaced))
                    .bold()
                    .foregroundColor(.blue)
                    .padding(10)
                    .frame(maxWidth: .infinity)
                    .background(Color(.systemBackground))
                    .cornerRadius(8)
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.blue.opacity(0.3), lineWidth: 1))
            }
            .padding(.horizontal)
            
            Spacer()
            
            // Main Action Button
            Button(action: {
                if cameraEngine.isStreaming {
                    cameraEngine.stopServer()
                } else {
                    cameraEngine.startServer()
                }
            }) {
                Text(cameraEngine.isStreaming ? "Stop Stream Server" : "Start Stream Server")
                    .bold()
                    .frame(maxWidth: .infinity)
                    .padding()
                    .background(cameraEngine.isStreaming ? Color.red : Color.green)
                    .foregroundColor(.white)
                    .cornerRadius(12)
            }
            .padding(.horizontal)
            .padding(.bottom, 16)
        }
        .onAppear {
            cameraEngine.refreshNetworkContext()
            cameraEngine.initializeHardwareLimits()
        }
    }
}

// Extension to add a clean inner shadow look to the camera preview box
extension View {
    func innerShadow(color: Color, radius: CGFloat, x: CGFloat, y: CGFloat) -> some View {
        self.overlay(
            RoundedRectangle(cornerRadius: 12)
                .stroke(color, lineWidth: radius)
                .blur(radius: radius)
                .offset(x: x, y: y)
                .mask(RoundedRectangle(cornerRadius: 12))
        )
    }
}

---
Dynamic Exposure & Preview Engine Core CodeThis logic handles the UIKit hosting layer for the camera preview while exposing the hardware configuration boundaries (ISO and Shutter limits) safely to SwiftUI. Prototype Example for Inspiration:

import AVFoundation
import UIKit
import HaishinKit

class AdaptiveStreamingEngine: NSObject, ObservableObject, AVCaptureVideoDataOutputSampleBufferDelegate {
    @Published var isStreaming = false
    @Published var rtspURLString = "rtsp://0.0.0.0:8554/live/golfcam"
    
    // Hardware Exposure Control Hooks
    @Published var shutterSpeed: Double = 0.001 // Default to 1/1000s
    @Published var isoValue: Float = 400.0
    
    var minShutterDuration: Double = 0.0001
    var maxShutterDuration: Double = 0.03
    var minISO: Float = 50.0
    var maxISO: Float = 1600.0
    
    let captureSession = AVCaptureSession()
    private let sessionQueue = DispatchQueue(label: "golf.adaptive.queue")
    private var rtspStream: RTMPStream!
    private var rtspServer: RTMPServer!
    private var targetDevice: AVCaptureDevice?
    
    func refreshNetworkContext() {
        let activeInterfaces = NetworkInterfaceManager.getAvailableIPAddresses()
        if let primaryInterface = activeInterfaces.first {
            self.rtspURLString = "rtsp://\(primaryInterface.ipAddress):8554/live/golfcam"
        }
    }
    
    func initializeHardwareLimits() {
        guard let device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back) else { return }
        self.targetDevice = device
        
        // Map native sensor structural configurations
        self.minShutterDuration = CMTimeGetSeconds(device.activeFormat.minExposureDuration)
        self.maxShutterDuration = CMTimeGetSeconds(device.activeFormat.maxExposureDuration)
        self.minISO = device.activeFormat.minISO
        self.maxISO = device.activeFormat.maxISO
        
        // Set optimal defaults for anti-blur golf metrics
        self.shutterSpeed = 0.0005 // 1/2000th of a second
        self.isoValue = device.formats.first?.maxISO ?? 400.0 / 2.0
    }
    
    func updateManualExposure() {
        guard let device = targetDevice else { return }
        sessionQueue.async {
            do {
                try device.lockForConfiguration()
                
                // Force explicit manual exposure lock, breaking auto-exposure logic completely
                let targetDuration = CMTime(value: 1, timescale: CMTimeScale(1.0 / self.shutterSpeed))
                device.setExposureModeCustom(duration: targetDuration, iso: self.isoValue, completionHandler: nil)
                
                device.unlockForConfiguration()
            } catch {
                print("Could not update manual exposure values: \(error)")
            }
        }
    }

    func startServer() {
        refreshNetworkContext()
        sessionQueue.async {
            let connection = RTMPConnection()
            self.rtspStream = RTMPStream(connection: connection)
            self.rtspStream.videoSettings = VideoCodecSettings(
                videoSize: .init(width: 1280, height: 720),
                bitRate: 5_000_000, 
                profileLevel: kVTProfileLevel_H264_High_AutoLevel as String,
                maxKeyFrameIntervalDuration: 1.0
            )
            
            self.captureSession.beginConfiguration()
            
            guard let device = self.targetDevice,
                  let videoInput = try? AVCaptureDeviceInput(device: device),
                  self.captureSession.canAddInput(videoInput) else { return }
            self.captureSession.addInput(videoInput)
            
            // Enforce locked 240 FPS Hardware mapping
            do {
                try device.lockForConfiguration()
                if let format = device.formats.first(where: { f in
                    f.videoSupportedFrameRateRanges.contains { $0.maxFrameRate >= 240.0 }
                }) {
                    device.activeFormat = format
                    let frameDuration = CMTime(value: 1, timescale: 240)
                    device.activeVideoMinFrameDuration = frameDuration
                    device.activeVideoMaxFrameDuration = frameDuration
                }
                device.unlockForConfiguration()
                
                // Establish manual shutter logic baseline
                self.updateManualExposure()
            } catch {
                print("Error setting frame rate hardware: \(error)")
            }
            
            let videoOutput = AVCaptureVideoDataOutput()
            videoOutput.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange]
            videoOutput.setSampleBufferDelegate(self, queue: self.sessionQueue)
            guard self.captureSession.canAddOutput(videoOutput) else { return }
            self.captureSession.addOutput(videoOutput)
            
            self.captureSession.commitConfiguration()
            self.captureSession.startRunning()
            
            self.rtspServer = RTMPServer()
            self.rtspServer.attachStream(self.rtspStream)
            self.rtspServer.startServer(port: 8554)
            
            DispatchQueue.main.async { self.isStreaming = true }
        }
    }
    
    func stopServer() {
        sessionQueue.async {
            self.captureSession.stopRunning()
            self.rtspServer?.stopServer()
            DispatchQueue.main.async { self.isStreaming = false }
        }
    }
    
    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        CVPixelBufferLockBaseAddress(pixelBuffer, .readOnly)
        if let cbCrPlaneAddress = CVPixelBufferGetBaseAddressOfPlane(pixelBuffer, 1) {
            let height = CVPixelBufferGetHeightOfPlane(pixelBuffer, 1)
            let bytesPerRow = CVPixelBufferGetBytesPerRowOfPlane(pixelBuffer, 1)
            memset(cbCrPlaneAddress, 128, height * bytesPerRow)
        }
        CVPixelBufferUnlockBaseAddress(pixelBuffer, .readOnly)
        self.rtspStream.appendSampleBuffer(sampleBuffer, withType: .video)
    }
}

---
Bridge Layer Prototype Code for Inspiration:

struct CameraPreviewContainer: AlignmentViewRepresentable, UIViewRepresentable {
    let session: AVCaptureSession
    
    func makeUIView(context: Context) -> UIView {
        let view = UIView(frame: .zero)
        let previewLayer = AVCaptureVideoPreviewLayer(session: session)
        previewLayer.videoGravity = .resizeAspectFill
        
        // Force the layout engine to fill the boundaries responsively
        view.layer.addSublayer(previewLayer)
        context.coordinator.previewLayer = previewLayer
        
        return view
    }
    
    func updateUIView(_ uiView: UIView, context: Context) {
        DispatchQueue.main.async {
            context.coordinator.previewLayer?.frame = uiView.bounds
        }
    }
    
    func makeCoordinator() -> Coordinator {
        Coordinator()
    }
    
    class Coordinator {
        var previewLayer: AVCaptureVideoPreviewLayer?
    }
}


---


NOTES:

* iPhone Possibilities? Use iPhone's camera in monochrome mode + higher FPS + LiDAR?

We use computer vision to recognize the club and the ball to help determine a shot is ready and about to happen. Possibly triggering the message to GSPro that a shot is ready.

Code for Another Project's Camera Support for a DIY Launch Monitor:
https://github.com/open-flight/openflight/blob/main/docs/development/camera-yolo.md

Lidar 3D Detection:
https://gitlab.kitware.com/LidarView/LidarView
https://github.com/open-mmlab/openPCDet

Computer Vision Possible Alternative?
https://docs.opencv.org/5.0/

This looks like a possible great option too:

https://www.amazon.com/ELP-Monochrome-Global-Shutter-Camera/dp/B0HCZVY6RM/ref=sr_1_9?dib=eyJ2IjoiMSJ9.iIRROg54-TPverDxKe9eDEU6TQoE7kOjwzNL4EBHy96MgKU0aBqnfVp0k6HaRok8DV4DS1-T1zNZP1WeUmRDrChfppmKCSPfMK2-kcmZOzLl8PiXSfg8C_rYGIJ_Mk6wUXJxR82dL-_pfJqX7QAI0tfVuYqnJLFKx1-0_NN02YLviX1TdveY97jgT7XgewOy-4fbfSTS_j4yKj1aVpFEuvujscKu3MNqmAp-ORaZyuOfofIQScm33KQ9lTpMdCUNhBtpbvKlK4zC6M0Qc26wTXdBk4yvm8AGJzfETeDysPc.G5f4WR_Ox3XQYwbLlq45uHKNfnP5VIqyI27LRm8v3TI&dib_tag=se&gad_source=1&hvadid=788574012457&hvdev=c&hvexpln=0&hvlocphy=9000976&hvnetw=g&hvocijid=14349071490299660448--&hvqmt=e&hvrand=14349071490299660448&hvtargid=kwd-1603517217725&hydadcr=25171_13824485&keywords=ov9281%2Bcamera&mcid=b72dac9b7c523f65a5b077bc4aab22b3&qid=1789486866&refinements=p_72%3A11192170011&rnid=11192166011&sr=8-9&th=1

-----
 
Notes about controlling the iPhone's Camera, Frame Rate, MonoChrome Output, LiDAR App Integration.

1. Camera & Frame Rate Control (AVFoundation)To change the camera's frame rate (FPS), you cannot rely on high-level session presets. Instead, you must query the AVCaptureDevice for its supported formats (AVFrameRateRange), lock the device configuration, and explicitly set the activeVideoMinFrameDuration and activeVideoMaxFrameDuration. [1] (https://developer.apple.com/documentation/avfoundation/capture-device-formats)2. Monochrome (Grayscale) OutputInstead of letting the camera capture color images and converting them later using filters (which wastes processing power), you can request the camera sensor natively stream in grayscale. This is done by setting the videoSettings pixel format type on your AVCaptureVideoDataOutput to kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange. By pulling only the Y-channel (Luminance) from this bi-planar frame, you get an ultra-fast, zero-overhead monochrome feed.3. LiDAR App IntegrationDepending on your project's goals, you have two native ways to pull LiDAR depth data:Via AVFoundation: Best for photography/videography. You can use .builtInLiDARDepthCamera as your device type to deliver depth maps synced frame-by-frame with your video feed.Via ARKit: Best for spatial mapping, tracking, and 3D point-cloud generation. ARFrame provides sceneDepth data natively. [1] (https://developer.apple.com/documentation/avfoundation/capturing-depth-using-the-lidar-camera), [2] (https://developer.apple.com/documentation/avfoundation/avcapturedevice/devicetype-swift.struct/builtinlidardepthcamera), [3] (https://hackernoon.com/arkit-and-lidar-building-point-clouds-in-swift-part-1), [4] (https://www.dynamsoft.com/codepool/ios-barcode-scanner-distance-measure.html)

----

The app must be able to connect and be used with WiFi OR a direct USB connection:

When a user plugs an iPhone into a PC via a USB cable and turns on Personal Hotspot (USB Only), or uses a Lightning/USB-C to Ethernet adapter, iOS treats that cable as a native network interface (en0 for Ethernet or p2p0/bridge0 for USB tethering). The app simply needs to detect the correct IP address for that active interface and host the RTSP server there.🌐 The Network Detection StrategyTo make this seamless for the user, your Swift code must query the iPhone's low-level network routing table. This allows the app to display the exact URL the PC software needs to connect to, regardless of whether they are on Wi-Fi or tethered by a cable.Here is the helper utility to extract all active local IP addresses:

----

Technical Notes about iPhone Camera & Video Feed:

ProtocolLatencyFrame Rate StabilityPC IngestionBest Fit ForRTSP (H.264)~100–150msVery StableNative in OpenCV, VLC, and most CV softwarePrimary Choice: Widely compatible with custom PC tracking software.SRT (H.264/H.265)<50msExcellentRequires ffmpeg/SRT libraries on PCUltimate CV Choice: Designed for low-latency video over erratic Wi-Fi.MJPEG over HTTP~50msDrops frames at 240 FPSSimple HTTP URLAvoid for 240 FPS: Too much CPU overhead compressing 240 independent JPEGs per second.

Possible Sample code snippets:

Example iPhone App Code:

import AVFoundation
import UIKit
import HaishinKit

class WebcamStreamingServer: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate {
    
    // HaishinKit objects for managing the stream and local server
    private var rtspStream: RTMPStream! // Note: HaishinKit uses RTMP/RTSP pipelines
    private var rtspServer: RTMPServer! // Hosts the local network endpoint
    
    private let captureSession = AVCaptureSession()
    private let sessionQueue = DispatchQueue(label: "webcam.stream.queue")
    
    func startHighSpeedWebcamServer() {
        sessionQueue.async {
            // 1. Initialize the Stream Engine
            let connection = RTMPConnection()
            self.rtspStream = RTMPStream(connection: connection)
            
            // Configure the H.264 encoder for high frame rate & low latency
            // Lowering bitrates slightly ensures the Wi-Fi network doesn't choke at 240 FPS
            self.rtspStream.videoSettings = VideoCodecSettings(
                videoSize: .init(width: 1280, height: 720), // 720p is mandatory for 240 FPS
                bitRate: 4_000_000, // 4 Mbps is plenty for monochrome 720p
                profileLevel: kVTProfileLevel_H264_High_AutoLevel as String,
                maxKeyFrameIntervalDuration: 1.0 // Frequent keyframes for fast CV recovery
            )
            
            self.captureSession.beginConfiguration()
            
            // 2. Select Back Camera
            guard let videoDevice = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back) else { return }
            guard let videoInput = try? AVCaptureDeviceInput(device: videoDevice),
                  self.captureSession.canAddInput(videoInput) else { return }
            self.captureSession.addInput(videoInput)
            
            // 3. Force 240 FPS Hardware Format
            do {
                try videoDevice.lockForConfiguration()
                if let format = videoDevice.formats.first(where: { f in
                    f.videoSupportedFrameRateRanges.contains { $0.maxFrameRate >= 240.0 }
                }) {
                    videoDevice.activeFormat = format
                    let frameDuration = CMTime(value: 1, timescale: 240)
                    videoDevice.activeVideoMinFrameDuration = frameDuration
                    videoDevice.activeVideoMaxFrameDuration = frameDuration
                }
                videoDevice.unlockForConfiguration()
            } catch {
                print("Failed locking FPS: \(error)")
            }
            
            // 4. Force Hardware Monochrome Output
            let videoOutput = AVCaptureVideoDataOutput()
            videoOutput.videoSettings = [
                kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
            ]
            videoOutput.setSampleBufferDelegate(self, queue: self.sessionQueue)
            guard self.captureSession.canAddOutput(videoOutput) else { return }
            self.captureSession.addOutput(videoOutput)
            
            self.captureSession.commitConfiguration()
            self.captureSession.startRunning()
            
            // 5. Start the local server on the iPhone
            // This exposes a URL like: rtsp://[iPhone-IP-Address]:8554/live/golfcam
            self.rtspServer = RTMPServer()
            self.rtspServer.attachStream(self.rtspStream)
            self.rtspServer.startServer(port: 8554) 
            print("Webcam feed live! Connect your PC software to port 8554")
        }
    }
    
    // 6. Inject the custom monochrome frames into the streaming encoder
    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        
        CVPixelBufferLockBaseAddress(pixelBuffer, .readOnly)
        
        // MONOCHROME COMPRESSION OPTIMIZATION:
        // Instead of wasting CPU encoding empty color data, we clear the chroma (Cb/Cr) channels
        // to zero. The H.264 encoder instantly recognizes there is zero color data, resulting
        // in massive bandwidth savings and crystal-clear high-contrast monochrome shapes.
        if let cbCrPlaneAddress = CVPixelBufferGetBaseAddressOfPlane(pixelBuffer, 1) {
            let height = CVPixelBufferGetHeightOfPlane(pixelBuffer, 1)
            let bytesPerRow = CVPixelBufferGetBytesPerRowOfPlane(pixelBuffer, 1)
            // Fill the chroma plane with 128 (neutral gray in YUV, representing no color)
            memset(cbCrPlaneAddress, 128, height * bytesPerRow)
        }
        
        CVPixelBufferUnlockBaseAddress(pixelBuffer, .readOnly)
        
        // Pass our clean, fast monochrome frame into the network stream
        self.rtspStream.appendSampleBuffer(sampleBuffer, withType: .video)
    }
}


Example Code for PC to Receive Data:

import cv2

# Point to your iPhone's local IP address
iphone_stream_url = "rtsp://1192.168.1.50:8554/live/golfcam"

# Open the network hardware stream
cap = cv2.VideoCapture(iphone_stream_url)

while True:
    ret, frame = cap.read()
    if not ret:
        break
        
    # 'frame' is already visually monochrome due to our iPhone optimization.
    # The PC can now run rapid ball tracking or trigger a rolling impact replay window.
    
    cv2.imshow('240 FPS Simulator Feed', frame)
    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

---

More prototype example code:

import SwiftUI
import AVFoundation

struct StreamingControlView: View {
    @StateObject private var cameraEngine = AdaptiveStreamingEngine()
    @State private var selectedConnectionMode: ConnectionMode = .cable
    
    enum ConnectionMode {
        case wifi, cable
    }
    
    var body: some View {
        VStack(spacing: 24) {
            // Header & Status
            VStack(spacing: 8) {
                Text("Golf Simulator WebCam Source")
                    .font(.title2)
                    .bold()
                
                HStack {
                    Circle()
                        .fill(cameraEngine.isStreaming ? Color.green : Color.red)
                        .frame(width: 12, height: 12)
                    Text(cameraEngine.isStreaming ? "Live Server Active" : "Server Stopped")
                        .font(.subheadline)
                        .foregroundColor(.secondary)
                }
            }
            .padding(.top)
            
            // Mode Selector
            Picker("Connection Mode", selection: $selectedConnectionMode) {
                Text("🔌 Wired Cable (Lowest Latency)").tag(ConnectionMode.cable)
                Text("📶 Wi-Fi Wireless").tag(ConnectionMode.wifi)
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
            
            // Connection Instructions Card
            VStack(alignment: .leading, spacing: 12) {
                if selectedConnectionMode == .cable {
                    Text("💡 **Wired Setup (Recommended for 240 FPS):**")
                        .font(.headline)
                    Text("1. Connect iPhone to PC via USB or Ethernet adapter.\n2. Enable **Personal Hotspot** (Set to USB Only) or verify Ethernet is active in iOS Settings.\n3. Enter the URL below into your PC software.")
                        .font(.footnote)
                        .foregroundColor(.secondary)
                } else {
                    Text("💡 **Wi-Fi Setup:**")
                        .font(.headline)
                    Text("1. Ensure both the iPhone and the Gaming PC are connected to the exact same 5GHz Wi-Fi Network.\n2. Enter the URL below into your PC software.")
                        .font(.footnote)
                        .foregroundColor(.secondary)
                }
            }
            .padding()
            .background(Color(.secondarySystemBackground))
            .cornerRadius(12)
            .padding(.horizontal)
            
            // Live URL Display Area
            VStack(spacing: 6) {
                Text("PC Video Stream Target URL:")
                    .font(.caption)
                    .foregroundColor(.secondary)
                    .textCase(.uppercase)
                
                Text(cameraEngine.rtspURLString)
                    .font(.system(.body, design: .monospaced))
                    .bold()
                    .foregroundColor(.blue)
                    .multilineTextAlignment(.center)
                    .padding()
                    .background(Color(.systemBackground))
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(Color.blue.opacity(0.3), lineWidth: 1)
                    )
                
                Text("Target Target Performance: **240 FPS @ 720p Monochrome**")
                    .font(.caption2)
                    .foregroundColor(.gray)
                    .padding(.top, 4)
            }
            .padding(.horizontal)
            
            Spacer()
            
            // Main Action Button
            Button(action: {
                if cameraEngine.isStreaming {
                    cameraEngine.stopServer()
                } else {
                    cameraEngine.startServer()
                }
            }) {
                Text(cameraEngine.isStreaming ? "Stop Stream Server" : "Start Stream Server")
                    .bold()
                    .frame(maxWidth: .infinity)
                    .padding()
                    .background(cameraEngine.isStreaming ? Color.red : Color.green)
                    .foregroundColor(.white)
                    .cornerRadius(12)
            }
            .padding(.horizontal)
            .padding(.bottom, 24)
        }
        .onAppear {
            cameraEngine.refreshNetworkContext()
        }
    }
}

// MARK: - Core Stream Engine Implementation
import HaishinKit

class AdaptiveStreamingEngine: NSObject, ObservableObject, AVCaptureVideoDataOutputSampleBufferDelegate {
    @Published var isStreaming = false
    @Published var rtspURLString = "rtsp://0.0.0.0:8554/live/golfcam"
    
    private var rtspStream: RTMPStream!
    private var rtspServer: RTMPServer!
    private let captureSession = AVCaptureSession()
    private let sessionQueue = DispatchQueue(label: "golf.adaptive.queue")
    private var activeIPAddress = "127.0.0.1"
    
    func refreshNetworkContext() {
        let activeInterfaces = NetworkInterfaceManager.getAvailableIPAddresses()
        if let primaryInterface = activeInterfaces.first {
            self.activeIPAddress = primaryInterface.ipAddress
            self.rtspURLString = "rtsp://\(primaryInterface.ipAddress):8554/live/golfcam"
        } else {
            self.rtspURLString = "rtsp://127.0.0.1:8554/live/golfcam (No Active Network)"
        }
    }
    
    func startServer() {
        refreshNetworkContext()
        
        sessionQueue.async {
            let connection = RTMPConnection()
            self.rtspStream = RTMPStream(connection: connection)
            
            // Standardize streaming presets for optimal computer vision throughput
            self.rtspStream.videoSettings = VideoCodecSettings(
                videoSize: .init(width: 1280, height: 720), // Essential for 240 FPS
                bitRate: 5_000_000, 
                profileLevel: kVTProfileLevel_H264_High_AutoLevel as String,
                maxKeyFrameIntervalDuration: 1.0
            )
            
            self.captureSession.beginConfiguration()
            
            guard let videoDevice = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back),
                  let videoInput = try? AVCaptureDeviceInput(device: videoDevice),
                  self.captureSession.canAddInput(videoInput) else { return }
            self.captureSession.addInput(videoInput)
            
            // Enforce 240 FPS Format
            do {
                try videoDevice.lockForConfiguration()
                if let format = videoDevice.formats.first(where: { f in
                    f.videoSupportedFrameRateRanges.contains { $0.maxFrameRate >= 240.0 }
                }) {
                    videoDevice.activeFormat = format
                    let frameDuration = CMTime(value: 1, timescale: 240)
                    videoDevice.activeVideoMinFrameDuration = frameDuration
                    videoDevice.activeVideoMaxFrameDuration = frameDuration
                }
                videoDevice.unlockForConfiguration()
            } catch {
                print("Error setting frame rate hardware: \(error)")
            }
            
            // Route stream data via standard BiPlanar data mapping for custom grayscale mapping
            let videoOutput = AVCaptureVideoDataOutput()
            videoOutput.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange]
            videoOutput.setSampleBufferDelegate(self, queue: self.sessionQueue)
            guard self.captureSession.canAddOutput(videoOutput) else { return }
            self.captureSession.addOutput(videoOutput)
            
            self.captureSession.commitConfiguration()
            self.captureSession.startRunning()
            
            self.rtspServer = RTMPServer()
            self.rtspServer.attachStream(self.rtspStream)
            self.rtspServer.startServer(port: 8554)
            
            DispatchQueue.main.async {
                self.isStreaming = true
            }
        }
    }
    
    func stopServer() {
        sessionQueue.async {
            self.captureSession.stopRunning()
            self.rtspServer?.stopServer()
            
            DispatchQueue.main.async {
                self.isStreaming = false
            }
        }
    }
    
    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        
        CVPixelBufferLockBaseAddress(pixelBuffer, .readOnly)
        
        // Zero out chroma layers to dramatically cut processing footprint and bandwidth overhead
        if let cbCrPlaneAddress = CVPixelBufferGetBaseAddressOfPlane(pixelBuffer, 1) {
            let height = CVPixelBufferGetHeightOfPlane(pixelBuffer, 1)
            let bytesPerRow = CVPixelBufferGetBytesPerRowOfPlane(pixelBuffer, 1)
            memset(cbCrPlaneAddress, 128, height * bytesPerRow)
        }
        
        CVPixelBufferUnlockBaseAddress(pixelBuffer, .readOnly)
        self.rtspStream.appendSampleBuffer(sampleBuffer, withType: .video)
    }
}
