from flask import Flask, Response, jsonify, request, send_from_directory
import gphoto2 as gp
import threading
import time
import re
import os
import subprocess
import smtplib
import atexit
import signal
import sys
from email.message import EmailMessage


# ==================================================
# D750 LIVEVIEW CONTROLLER
# VERSION 0.5.0
#
# Features:
# - Nikon D750 live view
# - Event-driven camera orientation (+ startup bootstrap)
# - Automatic browser live-view rotation
# - Exposure Preview control
# - Shutter control
# - Click-to-focus
# - D750 6016 x 4016 coordinate mapping
# - Shutter speed +/- control
# - Aperture +/- control
# - ISO +/- control
# - Manual Liveview on/off control
# - Liveview auto-off on last-viewer-disconnect / server shutdown
# - Single-widget config reads/writes (avoids full-tree USB round trips)
# - Live JPEG gallery (/gallery) - auto-downloads the JPEG half of each
#   RAW+JPEG capture as it's taken; RAW stays on the card untouched
# - Cloudflare Quick Tunnel remote access
# - Client email link delivery
# - Headless Raspberry Pi compatible
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
# CLOUDFLARE / EMAIL CONFIGURATION
# ==================================================

FLASK_PORT = 5000

SENDER_EMAIL = "number1nippon@gmail.com"

# NOTE: this key has been rotated. Even so, prefer loading secrets like
# this from an environment variable (os.environ["D750_APP_PASSWORD"])
# or a git-ignored config file rather than committing them, going forward.
SENDER_PASSWORD = "auiztmddimwzoazb"


# ==================================================
# GALLERY CONFIGURATION
# ==================================================

# Where downloaded JPEGs land. Set the camera itself to RAW + JPEG
# (Medium is a good size for this) via its own image quality menu - the
# RAW half of each capture is intentionally left on the card; only the
# JPEG gets pulled down here.
GALLERY_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "gallery")


# ==================================================
# D750 SENSOR COORDINATES
# ==================================================

SENSOR_WIDTH = 6016
SENSOR_HEIGHT = 4016


# ==================================================
# ORIENTATION STATE
# ==================================================

orientation_lock = threading.Lock()

# Nikon orientation mapping:
#
# 0 =   0 degrees
# 1 = 270 degrees
# 2 =  90 degrees
# 3 = 180 degrees

camera_orientation = 0


def orientation_degrees(value):
    return {0: 0, 1: 270, 2: 90, 3: 180}.get(value, 0)


def degrees_to_orientation(value):
    value = value % 360
    return {0: 0, 270: 1, 90: 2, 180: 3}.get(value)


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

    match = re.search(r'orientation.*?to\s+"?(-?\d+)', text, re.IGNORECASE)

    if not match:
        return None

    degrees = int(match.group(1))

    return degrees_to_orientation(degrees)


def camera_event_loop():
    print("Camera event listener started.")

    while not shutdown_event.is_set():
        try:
            with camera_lock:
                event_type, event_data = camera.wait_for_event(10)

            if event_data is None:
                continue

            if event_type == gp.GP_EVENT_FILE_ADDED:
                # A RAW+JPEG capture fires one of these per file. Handle
                # it outside camera_lock (it needs to re-acquire the lock
                # itself to download) so we don't hold up the poll loop.
                handle_new_camera_file(event_data)
                continue

            new_orientation = parse_orientation_event(event_data)

            if new_orientation is not None:
                set_camera_orientation(new_orientation)
                print("Orientation event:", event_data)

        except Exception as error:
            print("Camera event error:", error)


# ==================================================
# LIVE GALLERY
# ==================================================

gallery_lock = threading.Lock()
gallery_images = []  # list of {"filename": ..., "added_at": ...}

handled_camera_files_lock = threading.Lock()
handled_camera_files = set()  # "folder/name" keys already downloaded


def add_gallery_image(filename, added_at=None):
    with gallery_lock:
        gallery_images.append({
            "filename": filename,
            "added_at": added_at if added_at is not None else time.time()
        })


def load_existing_gallery_images():
    """Repopulate the in-memory gallery list from disk on startup, so a
    server restart mid-shoot doesn't lose already-downloaded photos."""

    os.makedirs(GALLERY_DIR, exist_ok=True)

    entries = []

    for filename in os.listdir(GALLERY_DIR):
        if filename.lower().endswith((".jpg", ".jpeg")):
            full_path = os.path.join(GALLERY_DIR, filename)
            entries.append((os.path.getmtime(full_path), filename))

    entries.sort()

    with gallery_lock:
        for added_at, filename in entries:
            gallery_images.append({"filename": filename, "added_at": added_at})

    print("[GALLERY] Loaded", len(entries), "existing image(s) from disk.")


def download_jpeg_from_camera(folder, name):
    """
    Downloads one file from the camera's storage into the gallery folder,
    if (and only if) it's a JPEG and hasn't already been downloaded.

    Must be called with camera_lock held.

    Returns True if a download happened, False if skipped (not a JPEG,
    or already handled).
    """

    extension = name.rsplit(".", 1)[-1].lower() if "." in name else ""

    if extension not in ("jpg", "jpeg"):
        return False

    key = folder + "/" + name

    with handled_camera_files_lock:
        if key in handled_camera_files:
            return False
        handled_camera_files.add(key)

    camera_file = camera.file_get(folder, name, gp.GP_FILE_TYPE_NORMAL)

    local_name = time.strftime("%Y%m%d-%H%M%S_") + name
    local_path = os.path.join(GALLERY_DIR, local_name)

    camera_file.save(local_path)

    add_gallery_image(local_name)

    print("[GALLERY] Saved:", local_name)

    return True


def handle_new_camera_file(file_path):
    """
    Called from camera_event_loop whenever the camera reports a newly
    written file via a FILE_ADDED event. This catches the SECOND file of
    a RAW+JPEG pair - the first is usually already returned directly by
    camera.capture() in the /capture route, which downloads it there
    instead (download_jpeg_from_camera's dedupe guard prevents this from
    double-downloading the same file if the camera fires an event for it
    too).
    """

    try:
        with camera_lock:
            download_jpeg_from_camera(file_path.folder, file_path.name)

    except Exception as error:
        print("[GALLERY] Error downloading new JPEG:", error)


@app.route("/gallery/list")
def gallery_list():
    with gallery_lock:
        images = sorted(gallery_images, key=lambda item: item["added_at"], reverse=True)

    return jsonify({
        "success": True,
        "images": [
            {"filename": img["filename"], "url": "/gallery/photos/" + img["filename"]}
            for img in images
        ]
    })


@app.route("/gallery/photos/<path:filename>")
def gallery_photo(filename):
    return send_from_directory(GALLERY_DIR, filename)


# ==================================================
# CAMERA INITIALISATION
# ==================================================

SETTING_WIDGETS = {
    "shutter": "shutterspeed",
    "aperture": "f-number",
    "iso": "iso"
}


def get_widget(name):
    """
    Fetch a single camera config widget.

    Uses the fast single-config API (avoids pulling/pushing the ENTIRE
    camera config tree over USB for a one-value change) when the
    installed python-gphoto2 / libgphoto2 supports it, and transparently
    falls back to a full config fetch otherwise.

    Must be called with camera_lock held.

    Returns (widget, full_config). full_config is None when the fast
    path was used - pass it straight into push_widget().
    """

    if hasattr(camera, "get_single_config"):
        return camera.get_single_config(name), None

    config = camera.get_config()
    widget = config.get_child_by_name(name)

    if widget is None:
        raise RuntimeError("Camera control not available: " + name)

    return widget, config


def push_widget(name, widget, full_config):
    """Push a widget change back to the camera. Must be called with camera_lock held."""

    if full_config is None and hasattr(camera, "set_single_config"):
        camera.set_single_config(name, widget)
    else:
        camera.set_config(full_config)


def cache_setting_choices():
    global setting_choices_cache

    try:
        with camera_lock:
            config = camera.get_config()

            for setting, widget_name in SETTING_WIDGETS.items():
                widget = config.get_child_by_name(widget_name)

                if widget is not None:
                    choices = [str(widget.get_choice(i)) for i in range(widget.count_choices())]
                    setting_choices_cache[setting] = choices

        print("Camera setting choices cached successfully.")

    except Exception as error:
        print("Error caching setting choices:", error)


def set_viewfinder(enabled):
    """Must be called with camera_lock held."""
    widget, full_config = get_widget("viewfinder")
    widget.set_value(1 if enabled else 0)
    push_widget("viewfinder", widget, full_config)


def bootstrap_orientation(timeout_seconds=3):
    """
    The D750 only fires an orientation event when the orientation
    CHANGES - it doesn't report current orientation on connect. That's
    why the page always used to start assuming landscape until the
    camera was physically rotated once.

    Many Nikon bodies emit an orientation event as a side effect of
    starting live view. Drain events for a few seconds right after
    enabling the viewfinder so that, if the camera does send one, we
    catch it before the server (and the page) render a single frame.

    Must be called with camera_lock held, before camera_event_loop starts.
    """

    print("Checking initial camera orientation...")

    deadline = time.time() + timeout_seconds

    while time.time() < deadline:
        try:
            event_type, event_data = camera.wait_for_event(300)
        except Exception as error:
            print("Orientation bootstrap error:", error)
            break

        if event_data is None:
            continue

        new_orientation = parse_orientation_event(event_data)

        if new_orientation is not None:
            set_camera_orientation(new_orientation)
            print("Initial orientation detected:", orientation_degrees(new_orientation), "degrees")
            return

    print(
        "No initial orientation event received - defaulting to 0 degrees. "
        "Rotating the camera will still correct this automatically."
    )


def initialise_camera():
    global camera

    print("Initialising camera...")

    camera = gp.Camera()
    camera.init()

    print("Camera connected.")

    cache_setting_choices()

    print("Camera ready. Liveview will start automatically when the first viewer connects.")


# ==================================================
# LIVEVIEW STATE / VIEWER TRACKING
# ==================================================

liveview_state_lock = threading.Lock()
liveview_active = False

connected_clients = 0
clients_lock = threading.Lock()

shutdown_event = threading.Event()

tunnel_process = None


def get_liveview_active():
    with liveview_state_lock:
        return liveview_active


def _set_liveview_active_flag(value):
    global liveview_active
    with liveview_state_lock:
        liveview_active = value


def enable_liveview_for_viewer():
    """
    Called when a viewer connects and liveview isn't already running -
    covers the very first connection after server start, and any
    reconnection after a dropped link (client can just click the emailed
    link again). Also re-runs the orientation bootstrap, since that only
    has something to detect while the viewfinder is actually starting.
    """

    if shutdown_event.is_set() or get_liveview_active():
        return

    try:
        with camera_lock:
            set_viewfinder(True)
            bootstrap_orientation()
        _set_liveview_active_flag(True)
        print("[LIVEVIEW] Viewer connected - liveview enabled.")
    except Exception as error:
        print("[LIVEVIEW] Error enabling liveview for viewer:", error)


def disable_liveview_no_viewers():
    if shutdown_event.is_set() or not get_liveview_active():
        return

    try:
        with camera_lock:
            set_viewfinder(False)
        _set_liveview_active_flag(False)
        print("[LIVEVIEW] No viewers connected - liveview disabled.")
    except Exception as error:
        print("[LIVEVIEW] Error disabling liveview after last viewer left:", error)


@app.route("/liveview/toggle", methods=["POST"])
def toggle_liveview():
    try:
        new_state = not get_liveview_active()

        with camera_lock:
            set_viewfinder(new_state)

        _set_liveview_active_flag(new_state)

        return jsonify({"success": True, "enabled": new_state})

    except Exception as error:
        return jsonify({"success": False, "error": str(error)}), 500


@app.route("/liveview/state")
def liveview_state_route():
    return jsonify({"success": True, "enabled": get_liveview_active()})


# ==================================================
# EXPOSURE PREVIEW
# ==================================================

def get_exposure_preview():
    widget, _ = get_widget("d1a5")
    return str(widget.get_value()) == "1"


def set_exposure_preview(enabled):
    global exposure_preview

    widget, full_config = get_widget("d1a5")
    widget.set_value("1" if enabled else "0")
    push_widget("d1a5", widget, full_config)

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

            # capture() returns the path of one of the two files from a
            # RAW+JPEG capture directly - it may not also arrive as a
            # FILE_ADDED event, depending on camera/driver behaviour. If
            # this one's the JPEG, grab it now; if it's the RAW, this is
            # a no-op and the JPEG will come in via the event loop
            # instead once the camera finishes writing it.
            try:
                download_jpeg_from_camera(path.folder, path.name)
            except Exception as error:
                print("[GALLERY] Error downloading captured JPEG:", error)

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

    widget, _ = get_widget(SETTING_WIDGETS[setting])

    return {
        "setting": setting,
        "value": str(widget.get_value()),
        "choices": setting_choices_cache.get(setting, [])
    }


def clamp(value, minimum, maximum):
    return max(minimum, min(maximum, value))


def change_setting(setting, direction):
    if setting not in SETTING_WIDGETS:
        raise RuntimeError("Unknown camera setting: " + str(setting))

    widget_name = SETTING_WIDGETS[setting]
    widget, full_config = get_widget(widget_name)

    current = str(widget.get_value())

    choices = setting_choices_cache.get(setting, [])
    if not choices:
        choices = [str(widget.get_choice(i)) for i in range(widget.count_choices())]
        setting_choices_cache[setting] = choices

    try:
        current_index = choices.index(current)
    except ValueError:
        raise RuntimeError("Current value not found in choices: " + current)

    new_index = clamp(current_index + direction, 0, len(choices) - 1)
    new_value = choices[new_index]

    widget.set_value(new_value)
    push_widget(widget_name, widget, full_config)

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
        if direction == "plus":
            step = 1
        elif direction == "minus":
            step = -1
        else:
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

def normalised_to_sensor(normal_x, normal_y, orientation):
    sensor_x = int(normal_x * SENSOR_WIDTH)
    sensor_y = int(normal_y * SENSOR_HEIGHT)

    sensor_x = clamp(sensor_x, 0, SENSOR_WIDTH - 1)
    sensor_y = clamp(sensor_y, 0, SENSOR_HEIGHT - 1)

    return (sensor_x, sensor_y)


def set_af_area(sensor_x, sensor_y):
    widget, full_config = get_widget("changeafarea")
    value = str(sensor_x) + "x" + str(sensor_y)
    widget.set_value(value)
    push_widget("changeafarea", widget, full_config)


def drive_autofocus():
    widget, full_config = get_widget("autofocusdrive")
    widget.set_value(1)
    push_widget("autofocusdrive", widget, full_config)


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

def camera_stream():
    global connected_clients

    with clients_lock:
        connected_clients += 1
        is_first_viewer = connected_clients == 1

    if is_first_viewer:
        enable_liveview_for_viewer()

    last_frame = None

    try:
        while True:

            if shutdown_event.is_set():
                break

            if not get_liveview_active():
                # Liveview is off - either the user turned it off, or the
                # camera's own idle timeout kicked in. Don't touch the
                # camera here at all: continuously calling
                # capture_preview() while liveview is off is exactly what
                # was forcing the D750 back into live view after its
                # 10-minute auto-off.
                time.sleep(0.2)
                continue

            jpeg = None
            got_lock = camera_lock.acquire(timeout=0.05)

            if got_lock:
                try:
                    preview = camera.capture_preview()
                    data = preview.get_data_and_size()
                    jpeg = bytes(data)
                    last_frame = jpeg
                except Exception as error:
                    print("[LIVEVIEW] Preview capture failed, disabling liveview:", error)
                    _set_liveview_active_flag(False)
                    jpeg = None
                finally:
                    camera_lock.release()

                if jpeg is None:
                    time.sleep(0.5)
                    continue
            else:
                # Camera is busy handling a setting/focus command right
                # now - reuse the last frame instead of stalling the
                # whole stream until the command finishes.
                jpeg = last_frame
                if jpeg is None:
                    time.sleep(0.03)
                    continue

            yield (
                b"--" + BOUNDARY + b"\r\n"
                b"Content-Type: image/jpeg\r\n"
                b"Content-Length: " + str(len(jpeg)).encode() + b"\r\n\r\n"
                + jpeg + b"\r\n"
            )

            time.sleep(0.03)

    except GeneratorExit:
        pass

    finally:
        with clients_lock:
            connected_clients -= 1
            if connected_clients < 0:
                connected_clients = 0
            should_disable = connected_clients == 0

        if should_disable:
            disable_liveview_no_viewers()


@app.route("/camera")
def camera_feed():
    return Response(camera_stream(), content_type="multipart/x-mixed-replace; boundary=frame")


# ==================================================
# CLOUDFLARE REMOTE ACCESS
# ==================================================

def get_client_email():
    while True:
        try:
            client_email = input("\nEnter the client's email address (or press Enter to cancel): ").strip()
        except EOFError:
            return None

        if not client_email:
            return None

        if "@" not in client_email:
            print("Invalid email address.")
            continue

        return client_email


def send_email(recipient_email, tunnel_url):
    msg = EmailMessage()

    msg["Subject"] = "Your D750 Live Camera Control Is Ready"
    msg["From"] = SENDER_EMAIL
    msg["To"] = recipient_email

    camera_link = tunnel_url

    msg.set_content(
        "Hello!\n\n"
        "The Nikon D750 remote camera control system is now online.\n\n"
        "Open the following link in your browser:\n\n"
        + camera_link +
        "\n\n"
        "You can use this page to view the live camera feed and operate the camera controls.\n"
    )

    try:
        with smtplib.SMTP_SSL("smtp.gmail.com", 465) as server:
            server.login(SENDER_EMAIL, SENDER_PASSWORD)
            server.send_message(msg)

        return (True, camera_link)

    except Exception as error:
        print("[EMAIL ERROR]", error)
        return (False, None)


def start_remote_access():
    global tunnel_process

    print("\n========================================")
    print("REMOTE ACCESS SETUP")
    print("========================================")

    recipient = get_client_email()

    if not recipient:
        print("[REMOTE ACCESS] Cancelled.")
        return

    print("\n[REMOTE ACCESS] Starting Cloudflare Quick Tunnel...")

    try:
        tunnel_process = subprocess.Popen(
            ["cloudflared", "tunnel", "--url", "http://localhost:" + str(FLASK_PORT)],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1
        )

        tunnel_url = None

        print("[REMOTE ACCESS] Waiting for Cloudflare URL...")

        while True:
            line = tunnel_process.stdout.readline()

            if not line:
                if tunnel_process.poll() is not None:
                    break
                continue

            print("[CLOUDFLARED]", line.rstrip())

            match = re.search(r"https://[a-zA-Z0-9.-]+\.trycloudflare\.com", line)

            if match:
                tunnel_url = match.group(0)
                break

        if not tunnel_url:
            print("[REMOTE ACCESS ERROR] Could not extract Cloudflare URL.")
            return

        print("\n[REMOTE ACCESS] Tunnel ready:")
        print(tunnel_url)

        success, camera_link = send_email(recipient, tunnel_url)

        print("\n========================================")

        if success:
            print("[SUCCESS] Camera link emailed to:")
            print(recipient)
            print("\nRemote camera URL:")
            print(camera_link)
        else:
            print("[ERROR] Tunnel is running, but the email could not be sent.")
            print("\nRemote camera URL:")
            print(tunnel_url)

        print("========================================")
        print("\n[REMOTE ACCESS] Tunnel remains active while this program is running.")

        while True:
            if tunnel_process.poll() is not None:
                print("[REMOTE ACCESS] Cloudflare tunnel stopped.")
                break
            time.sleep(1)

    except Exception as error:
        print("[REMOTE ACCESS ERROR]", error)

    finally:
        if tunnel_process is not None and tunnel_process.poll() is None:
            tunnel_process.terminate()
            try:
                tunnel_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                tunnel_process.kill()


# ==================================================
# SHUTDOWN HANDLING
# ==================================================

def shutdown_camera(*_args):
    if shutdown_event.is_set():
        return

    shutdown_event.set()

    print("\n[SHUTDOWN] Stopping camera and cleaning up...")

    try:
        if camera is not None and get_liveview_active():
            with camera_lock:
                set_viewfinder(False)
            _set_liveview_active_flag(False)
            print("[SHUTDOWN] Liveview disabled.")
    except Exception as error:
        print("[SHUTDOWN] Error disabling liveview:", error)

    try:
        if camera is not None:
            camera.exit()
            print("[SHUTDOWN] Camera connection closed.")
    except Exception as error:
        print("[SHUTDOWN] Error closing camera connection:", error)

    if tunnel_process is not None and tunnel_process.poll() is None:
        tunnel_process.terminate()
        try:
            tunnel_process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            tunnel_process.kill()

    print("[SHUTDOWN] Done.")


atexit.register(shutdown_camera)


# ==================================================
# WEB PAGE
# ==================================================

@app.route("/")
def home():
    return """
<!DOCTYPE html>
<html>
<head>
<title>D750 Camera Control v0.4.0</title>
<style>

html, body {
    margin: 0;
    padding: 0;
    width: 100%;
    height: 100%;
    background: black;
    overflow: hidden;
}

body {
    display: flex;
    justify-content: center;
    align-items: center;
}

#camera {
    width: 100vw;
    height: 100vh;
    object-fit: contain;
    display: block;
    cursor: crosshair;
    transform-origin: center center;
    transition: opacity 0.2s ease;
}

#camera.liveview-off {
    opacity: 0.25;
}

#focusBox {
    position: fixed;
    width: 60px;
    height: 60px;
    border: 2px solid red;
    border-radius: 4px;
    pointer-events: none;
    display: none;
    z-index: 500;
    box-sizing: border-box;
    transform: translate(-50%, -50%);
}

#controls {
    position: fixed;
    left: 20px;
    top: 50%;
    transform: translateY(-50%);
    z-index: 1000;
    display: flex;
    flex-direction: column;
    gap: 10px;
    max-height: 95vh;
    overflow-y: auto;
}

.control-button {
    width: 150px;
    padding: 12px 5px;
    font-size: 14px;
    background: #222;
    color: white;
    border: 2px solid white;
    border-radius: 8px;
    cursor: pointer;
}

.control-button:hover {
    background: #444;
}

.control-button:disabled {
    opacity: 0.5;
    cursor: wait;
}

.control-button.enabled {
    background: #164f16;
}

#shutterButton {
    background: #333;
}

#shutterButton:hover {
    background: #555;
}

.setting-control {
    width: 150px;
    color: white;
    font-family: Arial, sans-serif;
    background: rgba(0, 0, 0, 0.8);
    border: 1px solid white;
    border-radius: 8px;
    overflow: hidden;
}

.setting-name {
    font-size: 11px;
    padding: 4px;
    text-align: center;
    border-bottom: 1px solid #555;
}

.setting-value {
    font-size: 14px;
    padding: 6px;
    text-align: center;
    min-height: 16px;
    font-weight: bold;
}

.setting-buttons {
    display: flex;
}

.setting-button {
    flex: 1;
    padding: 6px 0;
    font-size: 16px;
    color: white;
    background: #222;
    border: 0;
    cursor: pointer;
}

.setting-button:first-child {
    border-right: 1px solid #555;
}

.setting-button:hover {
    background: #444;
}

.setting-button:disabled {
    opacity: 0.5;
    cursor: wait;
}

#orientationStatus {
    color: white;
    font-family: Arial, sans-serif;
    font-size: 12px;
    background: rgba(0, 0, 0, 0.7);
    padding: 5px;
    border-radius: 4px;
    text-align: center;
}

#focusStatus {
    color: white;
    font-family: Arial, sans-serif;
    font-size: 11px;
    background: rgba(0, 0, 0, 0.7);
    padding: 5px;
    border-radius: 4px;
    max-width: 150px;
    text-align: center;
}

</style>
</head>
<body>

<img id="camera" src="/camera">

<div id="focusBox"></div>

<div id="controls">

    <a href="/gallery" target="_blank" class="control-button" style="text-decoration: none; text-align: center; box-sizing: border-box; display: block;">
        Open Gallery &#8599;
    </a>

    <button id="liveviewButton" class="control-button" onclick="toggleLiveview()">
        Liveview: ...
    </button>

    <button id="exposureButton" class="control-button" onclick="toggleExposurePreview()">
        Exposure Preview: ...
    </button>

    <button id="shutterButton" class="control-button" onclick="captureImage()">
        SHUTTER
    </button>

    <div class="setting-control">
        <div class="setting-name">SHUTTER SPEED</div>
        <div id="shutterValue" class="setting-value">...</div>
        <div class="setting-buttons">
            <button class="setting-button" onclick="changeSetting('shutter', 'minus')">&minus;</button>
            <button class="setting-button" onclick="changeSetting('shutter', 'plus')">+</button>
        </div>
    </div>

    <div class="setting-control">
        <div class="setting-name">APERTURE</div>
        <div id="apertureValue" class="setting-value">...</div>
        <div class="setting-buttons">
            <button class="setting-button" onclick="changeSetting('aperture', 'minus')">&minus;</button>
            <button class="setting-button" onclick="changeSetting('aperture', 'plus')">+</button>
        </div>
    </div>

    <div class="setting-control">
        <div class="setting-name">ISO</div>
        <div id="isoValue" class="setting-value">...</div>
        <div class="setting-buttons">
            <button class="setting-button" onclick="changeSetting('iso', 'minus')">&minus;</button>
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

    let degrees =
        (orientation === 1) ? 270 :
        (orientation === 2) ? 90 :
        (orientation === 3) ? 180 : 0;

    status.innerText = "Orientation: " + degrees + "\u00b0";

    if (currentOrientation === orientation) {
        return;
    }

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
        .then(data => {
            if (data.success) {
                updateOrientation(data.orientation);
            }
        })
        .catch(error => console.error("Orientation:", error));
}

function updateSettingValue(setting, value) {
    const element = document.getElementById(setting + "Value");
    if (element) {
        element.innerText = value;
    }
}

function readSetting(setting) {
    fetch("/setting/" + setting, { cache: "no-store" })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                updateSettingValue(setting, data.value);
            }
        })
        .catch(error => console.error("Setting read error:", error));
}

function changeSetting(setting, direction) {
    const valueElement = document.getElementById(setting + "Value");
    const buttons = valueElement.parentElement.querySelectorAll("button");

    buttons.forEach(button => (button.disabled = true));

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
        .finally(() => buttons.forEach(button => (button.disabled = false)));
}

function positionFocusBox() {
    if (focusNormalX === null || focusNormalY === null) {
        return;
    }

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

    if (displayX < 0 || displayX > 1 || displayY < 0 || displayY > 1) {
        return;
    }

    let normalX;
    let normalY;

    if (currentOrientation === 0) {
        normalX = displayX;
        normalY = displayY;
    } else if (currentOrientation === 1) {
        normalX = 1.0 - displayY;
        normalY = displayX;
    } else if (currentOrientation === 2) {
        normalX = displayY;
        normalY = 1.0 - displayX;
    } else if (currentOrientation === 3) {
        normalX = 1.0 - displayX;
        normalY = 1.0 - displayY;
    } else {
        normalX = displayX;
        normalY = displayY;
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
            if (requestId !== focusRequestId) {
                return;
            }

            if (data.success) {
                status.innerText = "Focus: " + data.sensor_x + ", " + data.sensor_y + " @ " + data.degrees + "\u00b0";
            } else {
                status.innerText = "Focus error";
            }
        })
        .catch(error => {
            if (requestId !== focusRequestId) {
                return;
            }
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
        .then(data => {
            if (data.success) {
                updateExposureButton(data.enabled);
            }
        });
}

function toggleExposurePreview() {
    const button = document.getElementById("exposureButton");
    button.disabled = true;

    fetch("/toggle-exposure-preview", { method: "POST" })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                updateExposureButton(data.enabled);
            } else {
                alert("Exposure Preview error: " + data.error);
            }
        })
        .finally(() => (button.disabled = false));
}

function updateLiveviewButton(enabled) {
    const button = document.getElementById("liveviewButton");
    const cameraImg = document.getElementById("camera");

    if (enabled) {
        button.innerText = "Liveview: ON";
        button.classList.add("enabled");
    } else {
        button.innerText = "Liveview: OFF";
        button.classList.remove("enabled");
    }

    cameraImg.classList.toggle("liveview-off", !enabled);
}

function readLiveviewState() {
    fetch("/liveview/state", { cache: "no-store" })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                updateLiveviewButton(data.enabled);
            }
        })
        .catch(error => console.error("Liveview state error:", error));
}

function toggleLiveview() {
    const button = document.getElementById("liveviewButton");
    button.disabled = true;

    fetch("/liveview/toggle", { method: "POST" })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                updateLiveviewButton(data.enabled);
            } else {
                alert("Liveview error: " + data.error);
            }
        })
        .catch(error => console.error("Liveview toggle error:", error))
        .finally(() => (button.disabled = false));
}

function captureImage() {
    const button = document.getElementById("shutterButton");
    button.disabled = true;
    button.innerText = "CAPTURING...";

    fetch("/capture", { method: "POST" })
        .then(response => response.json())
        .then(data => {
            if (!data.success) {
                alert("Capture error: " + data.error);
            }
        })
        .finally(() => {
            button.disabled = false;
            button.innerText = "SHUTTER";
        });
}

document.getElementById("camera").addEventListener("click", clickToFocus);
window.addEventListener("resize", positionFocusBox);

readLiveviewState();
readExposurePreviewState();
readOrientation();
readSetting("shutter");
readSetting("aperture");
readSetting("iso");

setInterval(readOrientation, 2000);
setInterval(readLiveviewState, 5000);

</script>
</body>
</html>
"""


@app.route("/gallery")
def gallery_page():
    return """
<!DOCTYPE html>
<html>
<head>
<title>D750 Gallery</title>
<style>

html, body {
    margin: 0;
    padding: 0;
    background: #111;
    color: white;
    font-family: Arial, sans-serif;
}

#header {
    position: sticky;
    top: 0;
    z-index: 10;
    padding: 14px 20px;
    background: rgba(17, 17, 17, 0.95);
    border-bottom: 1px solid #333;
    display: flex;
    align-items: center;
    justify-content: space-between;
}

#header h1 {
    font-size: 16px;
    margin: 0;
    font-weight: normal;
}

#count {
    font-size: 12px;
    color: #999;
}

#empty {
    padding: 60px 20px;
    text-align: center;
    color: #888;
}

#grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
    gap: 4px;
    padding: 4px;
}

.thumb {
    aspect-ratio: 3 / 2;
    overflow: hidden;
    cursor: pointer;
    background: #222;
}

.thumb img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    display: block;
    transition: transform 0.15s ease;
}

.thumb:hover img {
    transform: scale(1.03);
}

#lightbox {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.92);
    display: none;
    align-items: center;
    justify-content: center;
    z-index: 100;
    cursor: zoom-out;
}

#lightbox.open {
    display: flex;
}

#lightbox img {
    max-width: 95vw;
    max-height: 92vh;
    object-fit: contain;
}

#lightboxClose {
    position: fixed;
    top: 16px;
    right: 24px;
    font-size: 28px;
    color: white;
    cursor: pointer;
    z-index: 101;
}

</style>
</head>
<body>

<div id="header">
    <h1>Live Gallery</h1>
    <div id="count">...</div>
</div>

<div id="empty" style="display: none;">
    No photos yet - they'll appear here automatically as they're taken.
</div>

<div id="grid"></div>

<div id="lightbox" onclick="closeLightbox()">
    <span id="lightboxClose">&times;</span>
    <img id="lightboxImg" src="">
</div>

<script>

let knownFilenames = new Set();

function openLightbox(url) {
    document.getElementById("lightboxImg").src = url;
    document.getElementById("lightbox").classList.add("open");
}

function closeLightbox() {
    document.getElementById("lightbox").classList.remove("open");
}

function addThumb(image, prepend) {
    const grid = document.getElementById("grid");

    const thumb = document.createElement("div");
    thumb.className = "thumb";

    const img = document.createElement("img");
    img.src = image.url;
    img.loading = "lazy";
    img.onclick = () => openLightbox(image.url);

    thumb.appendChild(img);

    if (prepend && grid.firstChild) {
        grid.insertBefore(thumb, grid.firstChild);
    } else {
        grid.appendChild(thumb);
    }
}

function refreshGallery() {
    fetch("/gallery/list", { cache: "no-store" })
        .then(response => response.json())
        .then(data => {
            if (!data.success) {
                return;
            }

            document.getElementById("count").innerText =
                data.images.length + (data.images.length === 1 ? " photo" : " photos");

            document.getElementById("empty").style.display =
                data.images.length === 0 ? "block" : "none";

            // data.images is newest-first. On first load, render the
            // whole set in order. After that, only ever prepend images
            // we haven't seen, so existing thumbnails never reload.
            const isFirstLoad = knownFilenames.size === 0;

            const newOnes = data.images.filter(image => !knownFilenames.has(image.filename));

            if (isFirstLoad) {
                data.images.forEach(image => {
                    knownFilenames.add(image.filename);
                    addThumb(image, false);
                });
            } else {
                // newOnes is newest-first; insert oldest-of-the-new first
                // so the very newest ends up at the very front.
                newOnes.slice().reverse().forEach(image => {
                    knownFilenames.add(image.filename);
                    addThumb(image, true);
                });
            }
        })
        .catch(error => console.error("Gallery list error:", error));
}

refreshGallery();
setInterval(refreshGallery, 4000);

</script>
</body>
</html>
"""


# ==================================================
# START SERVER
# ==================================================

if __name__ == "__main__":

    def handle_signal(signum, frame):
        shutdown_camera()
        sys.exit(0)

    signal.signal(signal.SIGINT, handle_signal)
    signal.signal(signal.SIGTERM, handle_signal)

    initialise_camera()
    load_existing_gallery_images()

    event_thread = threading.Thread(target=camera_event_loop, daemon=True)
    event_thread.start()

    remote_access_thread = threading.Thread(target=start_remote_access, daemon=True)
    remote_access_thread.start()

    print("[SERVER] Starting Flask camera controller...")
    print("[SERVER] Local address:")
    print("http://localhost:5000")

    app.run(host="0.0.0.0", port=FLASK_PORT, threaded=True)
