#include "httplib.h"
#include <gphoto2/gphoto2.h>
#include <mutex>
#include <thread>
#include <atomic>
#include <iostream>
#include <string>
#include <vector>
#include <algorithm>
#include <regex>
#include <chrono>

Camera* camera = nullptr;
GPContext* context = nullptr;
std::mutex camera_lock;
std::atomic<int> camera_orientation{0};

// Sensor dimensions for Nikon D750 touch-to-focus
const int SENSOR_WIDTH = 6016;
const int SENSOR_HEIGHT = 4016;

#define LOCK_CAMERA std::lock_guard<std::mutex> lock(camera_lock);

int orientation_degrees(int value) {
    switch (value) {
        case 1: return 270;
        case 2: return 90;
        case 3: return 180;
        default: return 0;
    }
}

int degrees_to_orientation(int degrees) {
    degrees = (degrees % 360 + 360) % 360;
    if (degrees == 270) return 1;
    if (degrees == 90) return 2;
    if (degrees == 180) return 3;
    return 0;
}

void set_camera_orientation(int value) {
    if (value < 0 || value > 3) return;
    int old_val = camera_orientation.load();
    if (old_val != value) {
        std::cout << "Camera orientation changed: " << orientation_degrees(old_val) 
                  << " -> " << orientation_degrees(value) << std::endl;
        camera_orientation.store(value);
    }
}

// Background camera event loop for orientation changes
void camera_event_loop() {
    std::cout << "Camera event listener started." << std::endl;
    while (true) {
        CameraEventType event_type;
        void* event_data = nullptr;
        {
            LOCK_CAMERA
            int ret = gp_camera_wait_for_event(camera, 10, &event_type, &event_data, context);
            if (ret != GP_OK) {
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
                continue;
            }
        }
        if (event_data != nullptr) {
            std::string text((const char*)event_data);
            if (text.find("d10e") != std::string::npos || text.find("orientation") != std::string::npos) {
                std::regex orient_regex(R"(orientation.*?to\s+\"?(-?\d+))", std::regex_constants::icase);
                std::smatch match;
                if (std::regex_search(text, match, orient_regex)) {
                    int degrees = std::stoi(match.str(1));
                    set_camera_orientation(degrees_to_orientation(degrees));
                }
            }
        }
    }
}

void initialize_camera() {
    gp_camera_new(&camera);
    context = gp_context_new();
    
    if (gp_camera_init(camera, context) != GP_OK) {
        std::cerr << "Failed to initialize camera." << std::endl;
    } else {
        std::cout << "Camera connected successfully." << std::endl;
    }

    LOCK_CAMERA
    CameraWidget *root = nullptr, *viewfinder = nullptr;
    if (gp_camera_get_config(camera, &root, context) == GP_OK) {
        if (gp_widget_get_child_by_name(root, "viewfinder", &viewfinder) == GP_OK) {
            int val = 1;
            gp_widget_set_value(viewfinder, &val);
            gp_camera_set_config(camera, root, context);
        }
        gp_widget_free(root);
    }
}

// Helper to get widget value string
std::string get_widget_value(const char* widget_name) {
    LOCK_CAMERA
    CameraWidget *root = nullptr, *child = nullptr;
    std::string val = "";
    if (gp_camera_get_config(camera, &root, context) == GP_OK) {
        if (gp_widget_get_child_by_name(root, widget_name, &child) == GP_OK) {
            char* str_val = nullptr;
            if (gp_widget_get_value(child, &str_val) == GP_OK && str_val) {
                val = std::string(str_val);
            }
        }
        gp_widget_free(root);
    }
    return val;
}

void set_widget_value(const char* widget_name, const std::string& val) {
    LOCK_CAMERA
    CameraWidget *root = nullptr, *child = nullptr;
    if (gp_camera_get_config(camera, &root, context) == GP_OK) {
        if (gp_widget_get_child_by_name(root, widget_name, &child) == GP_OK) {
            gp_widget_set_value(child, val.c_str());
            gp_camera_set_config(camera, root, context);
        }
        gp_widget_free(root);
    }
}

int main() {
    initialize_camera();

    std::thread event_thread(camera_event_loop);
    event_thread.detach();

    httplib::Server svr;

    // 1. Live View Stream
    svr.Get("/camera", [](const httplib::Request&, httplib::Response& res) {
        res.set_content_provider(
            "multipart/x-mixed-replace; boundary=frame",
            [](size_t /*offset*/, httplib::DataSink& sink) {
                CameraFile* file = nullptr;
                gp_file_new(&file);
                
                {
                    LOCK_CAMERA
                    if (gp_camera_capture_preview(camera, file, context) != GP_OK) {
                        gp_file_free(file);
                        std::this_thread::sleep_for(std::chrono::milliseconds(30));
                        return true;
                    }
                }

                const char* file_data;
                unsigned long file_size;
                gp_file_get_data_and_size(file, &file_data, &file_size);

                std::string chunk = "--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + 
                                    std::to_string(file_size) + "\r\n\r\n";
                
                sink.write(chunk.data(), chunk.size());
                sink.write(file_data, file_size);
                sink.write("\r\n", 2);

                gp_file_free(file);
                std::this_thread::sleep_for(std::chrono::milliseconds(30));
                return true;
            }
        );
    });

    // 2. Capture Image
    svr.Post("/capture", [](const httplib::Request&, httplib::Response& res) {
        CameraFilePath path;
        {
            LOCK_CAMERA
            if (gp_camera_capture(camera, GP_CAPTURE_IMAGE, &path, context) != GP_OK) {
                res.set_content(R"({"success": false, "error": "Capture failed"})", "application/json");
                return;
            }
        }
        std::string json = std::string(R"({"success": true, "folder": ")") + path.folder + 
                           R"(", "filename": ")" + path.name + "\"}";
        res.set_content(json, "application/json");
    });

    // 3. Orientation Status
    svr.Get("/orientation", [](const httplib::Request&, httplib::Response& res) {
        int orientation = camera_orientation.load();
        int degrees = orientation_degrees(orientation);
        std::string json = R"({"success": true, "orientation": )" + std::to_string(orientation) + 
                           R"(, "degrees": )" + std::to_string(degrees) + "}";
        res.set_content(json, "application/json");
    });

    // 4. Exposure Preview State
    svr.Get("/exposure-preview-state", [](const httplib::Request&, httplib::Response& res) {
        std::string val = get_widget_value("d1a5");
        bool enabled = (val == "1");
        std::string json = R"({"success": true, "enabled": )" + std::string(enabled ? "true" : "false") + "}";
        res.set_content(json, "application/json");
    });

    // 5. Toggle Exposure Preview
    svr.Post("/toggle-exposure-preview", [](const httplib::Request&, httplib::Response& res) {
        std::string val = get_widget_value("d1a5");
        bool current = (val == "1");
        std::string next_val = current ? "0" : "1";
        set_widget_value("d1a5", next_val);
        bool enabled = (get_widget_value("d1a5") == "1");
        std::string json = R"({"success": true, "enabled": )" + std::string(enabled ? "true" : "false") + "}";
        res.set_content(json, "application/json");
    });

    // 6. Camera Settings Read
    svr.Get(R"(/^\/setting\/(.+$))", [](const httplib::Request& req, httplib::Response& res) {
        std::string setting = req.matches[1];
        std::string widget_name = (setting == "shutter") ? "shutterspeed" : (setting == "aperture") ? "f-number" : (setting == "iso") ? "iso" : "";
        if (widget_name.empty()) {
            res.set_content(R"({"success": false, "error": "Unknown setting"})", "application/json");
            return;
        }
        std::string val = get_widget_value(widget_name.c_str());
        std::string json = R"({"success": true, "setting": ")" + setting + R"(", "value": ")" + val + "\"}";
        res.set_content(json, "application/json");
    });

    // 7. Click to Focus
    svr.Post("/focus", [](const httplib::Request& req, httplib::Response& res) {
        auto get_json_val = [](const std::string& body, const std::string& key) -> float {
            size_t pos = body.find(key);
            if (pos == std::string::npos) return 0.0f;
            pos = body.find(":", pos);
            if (pos == std::string::npos) return 0.0f;
            return std::stof(body.substr(pos + 1));
        };

        float normal_x = std::clamp(get_json_val(req.body, "x"), 0.0f, 1.0f);
        float normal_y = std::clamp(get_json_val(req.body, "y"), 0.0f, 1.0f);

        int orientation = camera_orientation.load();
        int s_x = std::clamp(static_cast<int>(normal_x * SENSOR_WIDTH), 0, SENSOR_WIDTH - 1);
        int s_y = std::clamp(static_cast<int>(normal_y * SENSOR_HEIGHT), 0, SENSOR_HEIGHT - 1);

        {
            LOCK_CAMERA
            CameraWidget *root = nullptr, *af_area = nullptr, *af_drive = nullptr;
            if (gp_camera_get_config(camera, &root, context) == GP_OK) {
                if (gp_widget_get_child_by_name(root, "changeafarea", &af_area) == GP_OK) {
                    std::string coord_str = std::to_string(s_x) + "x" + std::to_string(s_y);
                    gp_widget_set_value(af_area, coord_str.c_str());
                }
                if (gp_widget_get_child_by_name(root, "autofocusdrive", &af_drive) == GP_OK) {
                    int val = 1;
                    gp_widget_set_value(af_drive, &val);
                }
                gp_camera_set_config(camera, root, context);
                gp_widget_free(root);
            }
        }

        std::string json = R"({"success": true, "sensor_x": )" + std::to_string(s_x) + 
                           R"(, "sensor_y": )" + std::to_string(s_y) + 
                           R"(, "degrees": )" + std::to_string(orientation_degrees(orientation)) + "}";
        res.set_content(json, "application/json");
    });

    // 8. Serve Web Page UI
    svr.Get("/", [](const httplib::Request&, httplib::Response& res) {
        std::string html = R"raw(
<!DOCTYPE html>
<html>
<head>
<title>D750 Camera Control C++ v0.3.1</title>
<style>
html, body { margin: 0; padding: 0; width: 100%; height: 100%; background: black; overflow: hidden; }
body { display: flex; justify-content: center; align-items: center; }
#camera { width: 100vw; height: 100vh; object-fit: contain; display: block; cursor: crosshair; transform-origin: center center; }
#focusBox { position: fixed; width: 60px; height: 60px; border: 2px solid red; border-radius: 4px; pointer-events: none; display: none; z-index: 500; box-sizing: border-box; transform: translate(-50%, -50%); }
#controls { position: fixed; left: 20px; top: 50%; transform: translateY(-50%); z-index: 1000; display: flex; flex-direction: column; gap: 10px; max-height: 95vh; overflow-y: auto; }
.control-button { width: 150px; padding: 12px 5px; font-size: 14px; background: #222; color: white; border: 2px solid white; border-radius: 8px; cursor: pointer; }
.control-button:hover { background: #444; }
#exposureButton.enabled { background: #164f16; }
#shutterButton { background: #333; }
#shutterButton:hover { background: #555; }
.setting-control { width: 150px; color: white; font-family: Arial, sans-serif; background: rgba(0,0,0,0.8); border: 1px solid white; border-radius: 8px; overflow: hidden; }
.setting-name { font-size: 11px; padding: 4px; text-align: center; border-bottom: 1px solid #555; }
.setting-value { font-size: 14px; padding: 6px; text-align: center; min-height: 16px; font-weight: bold; }
#orientationStatus { color: white; font-family: Arial, sans-serif; font-size: 12px; background: rgba(0,0,0,0.7); padding: 5px; border-radius: 4px; text-align: center; }
#focusStatus { color: white; font-family: Arial, sans-serif; font-size: 11px; background: rgba(0,0,0,0.7); padding: 5px; border-radius: 4px; max-width: 150px; text-align: center; }
</style>
</head>
<body>
<img id="camera" src="/camera">
<div id="focusBox"></div>
<div id="controls">
    <button id="exposureButton" class="control-button" onclick="toggleExposurePreview()">Exposure Preview: ...</button>
    <button id="shutterButton" class="control-button" onclick="captureImage()">SHUTTER</button>
    <div class="setting-control">
        <div class="setting-name">SHUTTER SPEED</div>
        <div id="shutterValue" class="setting-value">...</div>
    </div>
    <div class="setting-control">
        <div class="setting-name">APERTURE</div>
        <div id="apertureValue" class="setting-value">...</div>
    </div>
    <div class="setting-control">
        <div class="setting-name">ISO</div>
        <div id="isoValue" class="setting-value">...</div>
    </div>
    <div id="orientationStatus">Orientation: ...</div>
    <div id="focusStatus">Click image to focus</div>
</div>
<script>
let currentOrientation = null;
let focusNormalX = null;
let focusNormalY = null;

function updateOrientation(orientation) {
    const status = document.getElementById("orientationStatus");
    let degrees = (orientation === 1) ? 270 : (orientation === 2) ? 90 : (orientation === 3) ? 180 : 0;
    status.innerText = "Orientation: " + degrees + "°";
    if (currentOrientation === orientation) return;
    currentOrientation = orientation;
    const cameraImg = document.getElementById("camera");
    if (orientation === 1 || orientation === 2) {
        cameraImg.style.transform = (orientation === 2) ? "rotate(90deg)" : "rotate(-90deg)";
    } else if (orientation === 3) {
        cameraImg.style.transform = "rotate(180deg)";
    } else {
        cameraImg.style.transform = "rotate(0deg)";
    }
}

function readOrientation() {
    fetch("/orientation", { cache: "no-store" })
    .then(r => r.json())
    .then(data => { if (data.success) updateOrientation(data.orientation); });
}

function readSetting(setting) {
    fetch("/setting/" + setting, { cache: "no-store" })
    .then(r => r.json())
    .then(data => { if (data.success) document.getElementById(setting + "Value").innerText = data.value; });
}

function clickToFocus(event) {
    const cameraImg = document.getElementById("camera");
    const rect = cameraImg.getBoundingClientRect();
    let displayX = (event.clientX - rect.left) / rect.width;
    let displayY = (event.clientY - rect.top) / rect.height;
    if (displayX < 0 || displayX > 1 || displayY < 0 || displayY > 1) return;

    let normalX = displayX, normalY = displayY;
    if (currentOrientation === 1) { normalX = 1.0 - displayY; normalY = displayX; }
    else if (currentOrientation === 2) { normalX = displayY; normalY = 1.0 - displayX; }
    else if (currentOrientation === 3) { normalX = 1.0 - displayX; normalY = 1.0 - displayY; }

    const focusBox = document.getElementById("focusBox");
    focusBox.style.left = (rect.left + (displayX * rect.width)) + "px";
    focusBox.style.top = (rect.top + (displayY * rect.height)) + "px";
    focusBox.style.display = "block";

    fetch("/focus", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ x: normalX, y: normalY })
    }).then(r => r.json()).then(data => {
        if (data.success) document.getElementById("focusStatus").innerText = "Focused: " + data.sensor_x + "," + data.sensor_y;
    });
}

function updateExposureButton(enabled) {
    const button = document.getElementById("exposureButton");
    button.innerText = enabled ? "Exposure Preview: ON" : "Exposure Preview: OFF";
    if (enabled) button.classList.add("enabled");
    else button.classList.remove("enabled");
}

function readExposurePreviewState() {
    fetch("/exposure-preview-state").then(r => r.json()).then(data => { if (data.success) updateExposureButton(data.enabled); });
}

function toggleExposurePreview() {
    fetch("/toggle-exposure-preview", { method: "POST" }).then(r => r.json()).then(data => { if (data.success) updateExposureButton(data.enabled); });
}

function captureImage() {
    const btn = document.getElementById("shutterButton");
    btn.disabled = true;
    btn.innerText = "CAPTURING...";
    fetch("/capture", { method: "POST" }).finally(() => { btn.disabled = false; btn.innerText = "SHUTTER"; });
}

document.getElementById("camera").addEventListener("click", clickToFocus);
readExposurePreviewState();
readOrientation();
readSetting("shutter");
readSetting("aperture");
readSetting("iso");
setInterval(readOrientation, 2000);
</script>
</body>
</html>
        )raw";
        res.set_content(html, "text/html");
    });

    std::cout << "Starting C++ Server on port 5000..." << std::endl;
    svr.listen("0.0.0.0", 5000);

    if (camera) {
        gp_camera_exit(camera, context);
        gp_camera_free(camera);
    }
    if (context) {
        gp_context_unref(context);
    }
    return 0;
}
