from flask import Flask, Response, jsonify, request
import gphoto2 as gp
import threading
import time
import re

# ==================================================
# D750 LIVEVIEW CONTROLLER
# VERSION 0.3.1 (With Setting Controls)
# ==================================================

app = Flask(__name__)

BOUNDARY = b"frame"

camera = None
camera_lock = threading.Lock()

exposure_preview = None

setting_choices_cache = {
    "shutter": [],
    "aperture": [],
    "iso": []
}

# ==================================================
# D750 SENSOR COORDINATES
# ==================================================

SENSOR_WIDTH = 6016
SENSOR_HEIGHT = 4016

# ==================================================
# ORIENTATION STATE
# ==================================================

orientation_lock = threading.Lock()
camera_orientation = 0

def orientation_degrees(value):
    return {
        0: 0,
        1: 270,
        2: 90,
        3: 180
    }.get(value, 0)

def degrees_to_orientation(value):
    value = value % 360
    return {
        0: 0,
        270: 1,
        90: 2,
        180: 3
    }.get(value)

def set_camera_orientation(value):
    global camera_orientation
    if value not in (0, 1, 2, 3):
        return
    with orientation_lock:
        if camera_orientation != value:
            print(
                "Camera orientation changed:",
                orientation_degrees(camera_orientation),
                "->",
                orientation_degrees(value)
            )
            camera_orientation = value

def get_camera_orientation():
    with orientation_lock:
        return camera_orientation

# ==================================================
# CAMERA EVENT LISTENER
# ==================================================

def parse_orientation_event(event_data):
    if event_data is None:
        return None
    text = str(event_data)
    if "d10e" not in text.lower():
        return None
    match = re.search(
        r'orientation.*?to\s+"?(-?\d+)',
        text,
        re.IGNORECASE
    )
    if not match:
        return None
    degrees = int(match.group(1))
    return degrees_to_orientation(degrees)

def camera_event_loop():
    print("Camera event listener started.")
    while True:
        try:
            with camera_lock:
                event_type, event_data = camera.wait_for_event(10)
            if event_data is None:
                continue
            new_orientation = parse_orientation_event(event_data)
            if new_orientation is not None:
                set_camera_orientation(new_orientation)
                print("Orientation event:", event_data)
        except Exception as error:
            print("Camera event error:", error)

# ==================================================
# CAMERA INITIALISATION & CACHING
# ==================================================

SETTING_WIDGETS = {
    "shutter": "shutterspeed",
    "aperture": "f-number",
    "iso": "iso"
}

def cache_setting_choices():
    global setting_choices_cache
    try:
        with camera_lock:
            config = camera.get_config()
            for setting, widget_name in SETTING_WIDGETS.items():
                widget = config.get_child_by_name(widget_name)
                if widget is not None:
                    choices = []
                    for index in range(widget.count_choices()):
                        choices.append(str(widget.get_choice(index)))
                    setting_choices_cache[setting] = choices
        print("Camera setting choices cached successfully.")
    except Exception as error:
        print("Error caching setting choices:", error)

def initialise_camera():
    global camera
    print("Initialising camera...")
    camera = gp.Camera()
    camera.init()
    print("Camera connected.")

    with camera_lock:
        config = camera.get_config()
        viewfinder = config.get_child_by_name("viewfinder")
        if viewfinder is None:
            raise RuntimeError("Could not find viewfinder control")
        viewfinder.set_value(1)
        camera.set_config(config)

    cache_setting_choices()
    print("Live View started.")

# ==================================================
# EXPOSURE PREVIEW
# ==================================================

def get_exposure_preview():
    config = camera.get_config()
    widget = config.get_child_by_name("d1a5")
    if widget is None:
        raise RuntimeError("Nikon Exposure Preview control d1a5 is not available")
    return str(widget.get_value()) == "1"

def set_exposure_preview(enabled):
    global exposure_preview
    config = camera.get_config()
    widget = config.get_child_by_name("d1a5")
    if widget is None:
        raise RuntimeError("Nikon Exposure Preview control d1a5 is not available")
    widget.set_value("1" if enabled else "0")
    camera.set_config(config)
    actual = get_exposure_preview()
    exposure_preview = actual
    return actual

@app.route("/toggle-exposure-preview", methods=["POST"])
def toggle_exposure_preview():
    try:
        with camera_lock:
            current = get_exposure_preview()
            actual = set_exposure_preview(not current)
        return jsonify({"success": True, "enabled": actual})
    except Exception as error:
        return jsonify({"success": False, "error": str(error)}), 500

@app.route("/exposure-preview-state")
def exposure_preview_state():
    try:
        with camera_lock:
            state = get_exposure_preview()
        return jsonify({"success": True, "enabled": state})
    except Exception as error:
        return jsonify({"success": False, "error": str(error)}), 500

# ==================================================
# SHUTTER CAPTURE
# ==================================================

@app.route("/capture", methods=["POST"])
def capture():
    try:
        print("Shutter pressed.")
        with camera_lock:
            path = camera.capture(gp.GP_CAPTURE_IMAGE)
        print("Image captured:", path.folder, path.name)
        return jsonify({"success": True, "folder": path.folder, "filename": path.name})
    except Exception as error:
        print("Capture error:", error)
        return jsonify({"success": False, "error": str(error)}), 500

# ==================================================
# CAMERA SETTING +/- CONTROLS
# ==================================================

def get_setting_state(setting):
    if setting not in SETTING_WIDGETS:
        raise RuntimeError("Unknown camera setting: " + str(setting))
    config = camera.get_config()
    widget = config.get_child_by_name(SETTING_WIDGETS[setting])
    if widget is None:
        raise RuntimeError("Camera setting not available: " + setting)
    return {
        "setting": setting,
        "value": str(widget.get_value()),
        "choices": setting_choices_cache.get(setting, [])
    }

def change_setting(setting, direction):
    if setting not in SETTING_WIDGETS:
        raise RuntimeError("Unknown camera setting: " + str(setting))
    
    config = camera.get_config()
    widget = config.get_child_by_name(SETTING_WIDGETS[setting])
    if widget is None:
        raise RuntimeError("Camera setting not available: " + setting)

    current = str(widget.get_value())
    choices = setting_choices_cache.get(setting, [])

    if not choices:
        for index in range(widget.count_choices()):
            choices.append(str(widget.get_choice(index)))
        setting_choices_cache[setting] = choices

    try:
        current_index = choices.index(current)
    except ValueError:
        raise RuntimeError("Current value not found in choices: " + current)

    new_index = clamp(current_index + direction, 0, len(choices) - 1)
    new_value = choices[new_index]
    
    widget.set_value(new_value)
    camera.set_config(config)
    return new_value

@app.route("/setting/<setting>")
def setting_state(setting):
    try:
        with camera_lock:
            state = get_setting_state(setting)
        return jsonify({"success": True, **state})
    except Exception as error:
        return jsonify({"success": False, "error": str(error)}), 500

@app.route("/setting/<setting>/<direction>", methods=["POST"])
def change_camera_setting(setting, direction):
    try:
        step = 1 if direction == "plus" else -1 if direction == "minus" else 0
        if step == 0:
            raise RuntimeError("Invalid direction: " + direction)
        with camera_lock:
            new_value = change_setting(setting, step)
        return jsonify({"success": True, "setting": setting, "value": new_value})
    except Exception as error:
        return jsonify({"success": False, "error": str(error)}), 500

# ==================================================
# ORIENTATION API
# ==================================================

@app.route("/orientation")
def orientation():
    value = get_camera_orientation()
    return jsonify({
        "success": True,
        "orientation": value,
        "degrees": orientation_degrees(value)
    })

# ==================================================
# CLICK TO FOCUS
# ==================================================

def clamp(value, minimum, maximum):
    return max(minimum, min(maximum, value))

def normalised_to_sensor(normal_x, normal_y, orientation):
    sensor_x = int(normal_x * SENSOR_WIDTH)
    sensor_y = int(normal_y * SENSOR_HEIGHT)
    sensor_x = clamp(sensor_x, 0, SENSOR_WIDTH - 1)
    sensor_y = clamp(sensor_y, 0, SENSOR_HEIGHT - 1)
    return (sensor_x, sensor_y)

def set_af_area(sensor_x, sensor_y):
    config = camera.get_config()
    widget = config.get_child_by_name("changeafarea")
    if widget is None:
        raise RuntimeError("Nikon Change AF Area control is not available")
    value = str(sensor_x) + "x" + str(sensor_y)
    widget.set_value(value)
    camera.set_config(config)

def drive_autofocus():
    config = camera.get_config()
    widget = config.get_child_by_name("autofocusdrive")
    if widget is None:
        raise RuntimeError("Nikon Autofocus Drive control is not available")
    widget.set_value(1)
    camera.set_config(config)

@app.route("/focus", methods=["POST"])
def focus():
    try:
        data = request.get_json(force=True)
        normal_x = clamp(float(data.get("x")), 0.0, 1.0)
        normal_y = clamp(float(data.get("y")), 0.0, 1.0)

        orientation = get_camera_orientation()
        sensor_x, sensor_y = normalised_to_sensor(
            normal_x, normal_y, orientation_degrees(orientation)
        )

        with camera_lock:
            set_af_area(sensor_x, sensor_y)
            drive_autofocus()

        return jsonify({
            "success": True,
            "normal_x": normal_x,
            "normal_y": normal_y,
            "sensor_x": sensor_x,
            "sensor_y": sensor_y,
            "orientation": orientation,
            "degrees": orientation_degrees(orientation)
        })
    except Exception as error:
        return jsonify({"success": False, "error": str(error)}), 500

# ==================================================
# LIVE VIEW STREAM
# ==================================================

# ==================================================
# LIVE VIEW STREAM
# ==================================================

def camera_stream():
    while True:
        try:
            with camera_lock:
                preview = camera.capture_preview()
                data = preview.get_data_and_size()
                jpeg = bytes(data)

            yield (
                b"--" + BOUNDARY + b"\r\n"
                b"Content-Type: image/jpeg\r\n"
                b"Content-Length: " + str(len(jpeg)).encode() + b"\r\n\r\n" +
                jpeg + b"\r\n"
            )
            
            # Throttle slightly to relieve USB congestion and stop control stutter
            time.sleep(0.03)
            
        except GeneratorExit:
            break
        except Exception as error:
            time.sleep(0.05)

@app.route("/camera")
def camera_feed():
    return Response(
        camera_stream(),
        content_type="multipart/x-mixed-replace; boundary=frame"
    )

# ==================================================
# WEB PAGE
# ==================================================

@app.route("/")
def home():
    return """
<!DOCTYPE html>
<html>
<head>
<title>D750 Camera Control v0.3.1</title>
<style>
html, body { margin: 0; padding: 0; width: 100%; height: 100%; background: black; overflow: hidden; }
body { display: flex; justify-content: center; align-items: center; }
#camera { width: 100vw; height: 100vh; object-fit: contain; display: block; cursor: crosshair; transform-origin: center center; }
#focusBox { position: fixed; width: 60px; height: 60px; border: 2px solid red; border-radius: 4px; pointer-events: none; display: none; z-index: 500; box-sizing: border-box; transform: translate(-50%, -50%); }
#controls { position: fixed; left: 20px; top: 50%; transform: translateY(-50%); z-index: 1000; display: flex; flex-direction: column; gap: 10px; max-height: 95vh; overflow-y: auto; }
.control-button { width: 150px; padding: 12px 5px; font-size: 14px; background: #222; color: white; border: 2px solid white; border-radius: 8px; cursor: pointer; }
.control-button:hover { background: #444; }
.control-button:disabled { opacity: 0.5; cursor: wait; }
#exposureButton.enabled { background: #164f16; }
#shutterButton { background: #333; }
#shutterButton:hover { background: #555; }
.setting-control { width: 150px; color: white; font-family: Arial, sans-serif; background: rgba(0,0,0,0.8); border: 1px solid white; border-radius: 8px; overflow: hidden; }
.setting-name { font-size: 11px; padding: 4px; text-align: center; border-bottom: 1px solid #555; }
.setting-value { font-size: 14px; padding: 6px; text-align: center; min-height: 16px; font-weight: bold; }
.setting-buttons { display: flex; }
.setting-button { flex: 1; padding: 6px 0; font-size: 16px; color: white; background: #222; border: 0; cursor: pointer; }
.setting-button:first-child { border-right: 1px solid #555; }
.setting-button:hover { background: #444; }
.setting-button:disabled { opacity: 0.5; cursor: wait; }
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
        <div class="setting-buttons">
            <button class="setting-button" onclick="changeSetting('shutter', 'minus')">−</button>
            <button class="setting-button" onclick="changeSetting('shutter', 'plus')">+</button>
        </div>
    </div>

    <div class="setting-control">
        <div class="setting-name">APERTURE</div>
        <div id="apertureValue" class="setting-value">...</div>
        <div class="setting-buttons">
            <button class="setting-button" onclick="changeSetting('aperture', 'minus')">−</button>
            <button class="setting-button" onclick="changeSetting('aperture', 'plus')">+</button>
        </div>
    </div>

    <div class="setting-control">
        <div class="setting-name">ISO</div>
        <div id="isoValue" class="setting-value">...</div>
        <div class="setting-buttons">
            <button class="setting-button" onclick="changeSetting('iso', 'minus')">−</button>
            <button class="setting-button" onclick="changeSetting('iso', 'plus')">+</button>
        </div>
    </div>

    <div id="orientationStatus">Orientation: ...</div>
    <div id="focusStatus">Click image to focus</div>
</div>

<script>
let currentOrientation = null;
let focusNormalX = null;
let focusNormalY = null;
let focusRequestId = 0;

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
    requestAnimationFrame(positionFocusBox);
}

function readOrientation() {
    fetch("/orientation", { cache: "no-store" })
    .then(response => response.json())
    .then(data => { if (data.success) updateOrientation(data.orientation); })
    .catch(error => console.error("Orientation:", error));
}

function updateSettingValue(setting, value) {
    const element = document.getElementById(setting + "Value");
    if (element) element.innerText = value;
}

function readSetting(setting) {
    fetch("/setting/" + setting, { cache: "no-store" })
    .then(response => response.json())
    .then(data => { if (data.success) updateSettingValue(setting, data.value); })
    .catch(error => console.error("Setting read error:", error));
}

function changeSetting(setting, direction) {
    const valueElement = document.getElementById(setting + "Value");
    const buttons = valueElement.parentElement.querySelectorAll("button");
    buttons.forEach(button => button.disabled = true);

    fetch("/setting/" + setting + "/" + direction, { method: "POST" })
    .then(response => response.json())
    .then(data => {
        if (data.success) {
            updateSettingValue(setting, data.value);
        } else {
            alert("Camera setting error: " + data.error);
        }
    })
    .catch(error => console.error("Camera setting error:", error))
    .finally(() => buttons.forEach(button => button.disabled = false));
}

function positionFocusBox() {
    if (focusNormalX === null || focusNormalY === null) return;
    const cameraImg = document.getElementById("camera");
    const focusBox = document.getElementById("focusBox");
    const rect = cameraImg.getBoundingClientRect();
    focusBox.style.left = (rect.left + (focusNormalX * rect.width)) + "px";
    focusBox.style.top = (rect.top + (focusNormalY * rect.height)) + "px";
    focusBox.style.display = "block";
}

function clickToFocus(event) {
    const requestId = ++focusRequestId;
    const cameraImg = document.getElementById("camera");
    const rect = cameraImg.getBoundingClientRect();
    let displayX = (event.clientX - rect.left) / rect.width;
    let displayY = (event.clientY - rect.top) / rect.height;
    if (displayX < 0 || displayX > 1 || displayY < 0 || displayY > 1) return;

    let normalX, normalY;
    if (currentOrientation === 0) {
        normalX = displayX; normalY = displayY;
    } else if (currentOrientation === 1) {
        normalX = 1.0 - displayY; normalY = displayX;
    } else if (currentOrientation === 2) {
        normalX = displayY; normalY = 1.0 - displayX;
    } else if (currentOrientation === 3) {
        normalX = 1.0 - displayX; normalY = 1.0 - displayY;
    } else {
        normalX = displayX; normalY = displayY;
    }

    focusNormalX = displayX;
    focusNormalY = displayY;
    positionFocusBox();

    const status = document.getElementById("focusStatus");
    status.innerText = "Focusing...";

    fetch("/focus", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ x: normalX, y: normalY })
    })
    .then(response => response.json())
    .then(data => {
        if (requestId !== focusRequestId) return;
        if (data.success) {
            status.innerText = "Focus: " + data.sensor_x + ", " + data.sensor_y + " @ " + data.degrees + "°";
        } else {
            status.innerText = "Focus error";
        }
    })
    .catch(error => {
        if (requestId !== focusRequestId) return;
        status.innerText = "Focus error";
    });
}

function updateExposureButton(enabled) {
    const button = document.getElementById("exposureButton");
    if (enabled) {
        button.innerText = "Exposure Preview: ON";
        button.classList.add("enabled");
    } else {
        button.innerText = "Exposure Preview: OFF";
        button.classList.remove("enabled");
    }
}

function readExposurePreviewState() {
    fetch("/exposure-preview-state")
    .then(response => response.json())
    .then(data => { if (data.success) updateExposureButton(data.enabled); });
}

function toggleExposurePreview() {
    const button = document.getElementById("exposureButton");
    button.disabled = true;
    fetch("/toggle-exposure-preview", { method: "POST" })
    .then(response => response.json())
    .then(data => {
        if (data.success) updateExposureButton(data.enabled);
        else alert("Exposure Preview error: " + data.error);
    })
    .finally(() => button.disabled = false);
}

function captureImage() {
    const button = document.getElementById("shutterButton");
    button.disabled = true;
    button.innerText = "CAPTURING...";
    fetch("/capture", { method: "POST" })
    .then(response => response.json())
    .then(data => {
        if (!data.success) alert("Capture error: " + data.error);
    })
    .finally(() => {
        button.disabled = false;
        button.innerText = "SHUTTER";
    });
}

document.getElementById("camera").addEventListener("click", clickToFocus);
window.addEventListener("resize", positionFocusBox);

readExposurePreviewState();
readOrientation();
readSetting("shutter");
readSetting("aperture");
readSetting("iso");
setInterval(readOrientation, 2000);
</script>
</body>
</html>
"""

if __name__ == "__main__":
    initialise_camera()

    event_thread = threading.Thread(
        target=camera_event_loop,
        daemon=True
    )
    event_thread.start()

    app.run(
        host="0.0.0.0",
        port=5000,
        threaded=True
    )
