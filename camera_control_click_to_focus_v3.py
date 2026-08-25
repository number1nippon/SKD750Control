from flask import Flask, Response, jsonify, request
import gphoto2 as gp
import threading
import time
import re


# ==================================================
# D750 LIVEVIEW CONTROLLER
# VERSION 0.2.1
#
# Coordinate Transform Build
#
# Features:
# - Nikon D750 live view
# - Event-driven camera orientation
# - Automatic browser live-view rotation
# - Exposure Preview control
# - Shutter control
# - Click-to-focus
# - D750 6016 x 4016 coordinate mapping
# - Browser-size-independent click coordinates
# - Orientation-aware coordinate transforms
# ==================================================


app = Flask(__name__)

BOUNDARY = b"frame"

camera = None
camera_lock = threading.Lock()

exposure_preview = None


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
# Internal value -> physical camera orientation
#
# 0 =   0 degrees
# 1 = 270 degrees
# 2 =  90 degrees
# 3 = 180 degrees

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
                orientation_degrees(
                    camera_orientation
                ),
                "->",
                orientation_degrees(
                    value
                )
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

    degrees = int(
        match.group(1)
    )

    return degrees_to_orientation(
        degrees
    )


def camera_event_loop():

    print(
        "Camera event listener started."
    )

    while True:

        try:

            with camera_lock:

                event_type, event_data = (
                    camera.wait_for_event(
                        10
                    )
                )

            if event_data is None:

                continue

            new_orientation = (
                parse_orientation_event(
                    event_data
                )
            )

            if new_orientation is not None:

                set_camera_orientation(
                    new_orientation
                )

                print(
                    "Orientation event:",
                    event_data
                )

        except Exception as error:

            print(
                "Camera event error:",
                error
            )

# ==================================================
# CAMERA INITIALISATION
# ==================================================

def initialise_camera():

    global camera

    print(
        "Initialising camera..."
    )

    camera = gp.Camera()

    camera.init()

    print(
        "Camera connected."
    )

    config = camera.get_config()

    viewfinder = (
        config.get_child_by_name(
            "viewfinder"
        )
    )

    if viewfinder is None:

        raise RuntimeError(
            "Could not find viewfinder control"
        )

    viewfinder.set_value(
        1
    )

    camera.set_config(
        config
    )

    print(
        "Live View started."
    )


# ==================================================
# EXPOSURE PREVIEW
# ==================================================

def get_exposure_preview():

    config = camera.get_config()

    widget = (
        config.get_child_by_name(
            "d1a5"
        )
    )

    if widget is None:

        raise RuntimeError(
            "Nikon Exposure Preview control d1a5 "
            "is not available in this libgphoto2 configuration"
        )

    value = widget.get_value()

    print(
        "Exposure Preview current value:",
        value
    )

    return str(value) == "1"


def set_exposure_preview(enabled):

    global exposure_preview

    config = camera.get_config()

    widget = (
        config.get_child_by_name(
            "d1a5"
        )
    )

    if widget is None:

        raise RuntimeError(
            "Nikon Exposure Preview control d1a5 "
            "is not available in this libgphoto2 configuration"
        )

    value = (
        "1"
        if enabled
        else "0"
    )

    print(
        "Setting Exposure Preview to:",
        value
    )

    widget.set_value(
        value
    )

    camera.set_config(
        config
    )

    actual = (
        get_exposure_preview()
    )

    exposure_preview = actual

    return actual


@app.route(
    "/toggle-exposure-preview",
    methods=["POST"]
)
def toggle_exposure_preview():

    try:

        with camera_lock:

            current = (
                get_exposure_preview()
            )

            actual = (
                set_exposure_preview(
                    not current
                )
            )

        return jsonify(
            {
                "success": True,
                "enabled": actual
            }
        )

    except Exception as error:

        print(
            "Exposure Preview error:",
            error
        )

        return jsonify(
            {
                "success": False,
                "error": str(error)
            }
        ), 500


@app.route(
    "/exposure-preview-state"
)
def exposure_preview_state():

    try:

        with camera_lock:

            state = (
                get_exposure_preview()
            )

        return jsonify(
            {
                "success": True,
                "enabled": state
            }
        )

    except Exception as error:

        return jsonify(
            {
                "success": False,
                "error": str(error)
            }
        ), 500


# ==================================================
# SHUTTER
# ==================================================

@app.route(
    "/capture",
    methods=["POST"]
)
def capture():

    try:

        print(
            "Shutter pressed."
        )

        with camera_lock:

            path = camera.capture(
                gp.GP_CAPTURE_IMAGE
            )

        print(
            "Image captured:",
            path.folder,
            path.name
        )

        return jsonify(
            {
                "success": True,
                "folder": path.folder,
                "filename": path.name
            }
        )

    except Exception as error:

        print(
            "Capture error:",
            error
        )

        return jsonify(
            {
                "success": False,
                "error": str(error)
            }
        ), 500


# ==================================================
# ORIENTATION API
# ==================================================

@app.route(
    "/orientation"
)
def orientation():

    value = (
        get_camera_orientation()
    )

    return jsonify(
        {
            "success": True,
            "orientation": value,
            "degrees": orientation_degrees(
                value
            )
        }
    )


# ==================================================
# CLICK TO FOCUS
# ==================================================

def clamp(value, minimum, maximum):

    return max(
        minimum,
        min(
            maximum,
            value
        )
    )


def normalised_to_sensor(
    normal_x,
    normal_y,
    orientation
):

    #
    # normal_x / normal_y now represent
    # coordinates in the ORIGINAL, UNROTATED
    # 640 x 480 live-view frame.
    #
    # The browser is responsible for undoing
    # the display rotation before sending them.
    #
    # Therefore the mapping to the D750 sensor
    # is direct for every orientation.
    #

    sensor_x = int(
        normal_x * SENSOR_WIDTH
    )

    sensor_y = int(
        normal_y * SENSOR_HEIGHT
    )

    sensor_x = clamp(
        sensor_x,
        0,
        SENSOR_WIDTH - 1
    )

    sensor_y = clamp(
        sensor_y,
        0,
        SENSOR_HEIGHT - 1
    )

    return (
        sensor_x,
        sensor_y
    )



def set_af_area(
    sensor_x,
    sensor_y
):

    config = camera.get_config()

    widget = (
        config.get_child_by_name(
            "changeafarea"
        )
    )

    if widget is None:

        raise RuntimeError(
            "Nikon Change AF Area control "
            "is not available"
        )

    value = (
        str(sensor_x)
        +
        "x"
        +
        str(sensor_y)
    )

    print(
        "Setting AF area:"
    )

    print(
        "  Sensor X:",
        sensor_x
    )

    print(
        "  Sensor Y:",
        sensor_y
    )

    print(
        "  ChangeAfArea value:",
        value
    )

    widget.set_value(
        value
    )

    print(
        "  Widget value after set:",
        widget.get_value()
    )

    camera.set_config(
        config
    )


def drive_autofocus():

    config = camera.get_config()

    widget = (
        config.get_child_by_name(
            "autofocusdrive"
        )
    )

    if widget is None:

        raise RuntimeError(
            "Nikon Autofocus Drive control "
            "is not available"
        )

    print(
        "Driving autofocus."
    )

    widget.set_value(
        1
    )

    camera.set_config(
        config
    )


@app.route(
    "/focus",
    methods=["POST"]
)
def focus():

    try:

        data = request.get_json(
            force=True
        )

        normal_x = float(
            data.get(
                "x"
            )
        )

        normal_y = float(
            data.get(
                "y"
            )
        )


        #
        # Normalised coordinates must
        # remain inside the image.
        #

        normal_x = clamp(
            normal_x,
            0.0,
            1.0
        )

        normal_y = clamp(
            normal_y,
            0.0,
            1.0
        )


        #
        # Get current physical camera
        # orientation.
        #

        orientation = (
            get_camera_orientation()
        )


        #
        # Convert the displayed click
        # into D750 sensor coordinates.
        #

        sensor_x, sensor_y = (
            normalised_to_sensor(
                normal_x,
                normal_y,
                orientation_degrees(
            orientation
        )
    )
)


        print(
            "Focus request:"
        )

        print(
            "  Browser X:",
            normal_x
        )

        print(
            "  Browser Y:",
            normal_y
        )

        print(
            "  Orientation:",
            orientation
        )

        print(
            "  Orientation degrees:",
            orientation_degrees(
                orientation
            )
        )


        lock_start = time.monotonic()

        with camera_lock:

            print(
                "FOCUS LOCK WAIT:",
                round(
                    time.monotonic()
                    -
                    lock_start,
                    3
                )
            )

            set_af_area(
                sensor_x,
                sensor_y
            )

            drive_autofocus()


        return jsonify(
            {
                "success": True,
                "normal_x": normal_x,
                "normal_y": normal_y,
                "sensor_x": sensor_x,
                "sensor_y": sensor_y,
                "orientation": orientation,
                "degrees": orientation_degrees(
                    orientation
                )
            }
        )

    except Exception as error:

        print(
            "Focus error:",
            error
        )

        return jsonify(
            {
                "success": False,
                "error": str(error)
            }
        ), 500


# ==================================================
# LIVE VIEW STREAM
# ==================================================

def camera_stream():

    while True:

        try:

            with camera_lock:

                preview = (
                    camera.capture_preview()
                )

                data = (
                    preview.get_data_and_size()
                )

                jpeg = bytes(
                    data
                )


            yield (
                b"--"
                +
                BOUNDARY
                +
                b"\r\n"
                +
                b"Content-Type: image/jpeg\r\n"
                +
                b"Content-Length: "
                +
                str(
                    len(jpeg)
                ).encode()
                +
                b"\r\n\r\n"
                +
                jpeg
                +
                b"\r\n"
            )


        except GeneratorExit:

            break


        except Exception as error:

            print(
                "Live View error:",
                error
            )

            time.sleep(
                0.01
            )


@app.route(
    "/camera"
)
def camera_feed():

    return Response(
        camera_stream(),
        content_type=
            "multipart/x-mixed-replace; boundary=frame"
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

<title>D750 Camera Control v0.2.1</title>

<style>

html,
body {

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


/* ----------------------------------------------
   CAMERA VIEW
   ---------------------------------------------- */

#camera {

    width: 100vw;
    height: 100vh;

    object-fit: contain;

    display: block;

    cursor: crosshair;

    transform-origin:
        center center;
}


/* ----------------------------------------------
   FOCUS BOX
   ---------------------------------------------- */

#focusBox {

    position: fixed;

    width: 60px;
    height: 60px;

    border:
        2px solid red;

    border-radius: 4px;

    pointer-events: none;

    display: none;

    z-index: 500;

    box-sizing:
        border-box;

    transform:
        translate(
            -50%,
            -50%
        );
}


/* ----------------------------------------------
   CONTROLS
   ---------------------------------------------- */

#controls {

    position: fixed;

    left: 20px;
    top: 50%;

    transform:
        translateY(-50%);

    z-index: 1000;

    display: flex;

    flex-direction: column;

    gap: 10px;
}


.control-button {

    width: 110px;

    padding:
        15px 5px;

    font-size: 14px;

    background: #222;

    color: white;

    border:
        2px solid white;

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


#exposureButton.enabled {

    background: #164f16;
}


#shutterButton {

    background: #333;
}


#shutterButton:hover {

    background: #555;
}


#orientationStatus {

    color: white;

    font-family:
        Arial,
        sans-serif;

    font-size: 12px;

    background:
        rgba(
            0,
            0,
            0,
            0.7
        );

    padding: 5px;

    border-radius: 4px;
}


#focusStatus {

    color: white;

    font-family:
        Arial,
        sans-serif;

    font-size: 11px;

    background:
        rgba(
            0,
            0,
            0,
            0.7
        );

    padding: 5px;

    border-radius: 4px;

    max-width: 180px;
}

</style>

</head>

<body>


<img
    id="camera"
    src="/camera"
>


<div
    id="focusBox"
></div>


<div id="controls">


<button
    id="exposureButton"
    class="control-button"
    onclick="toggleExposurePreview()"
>

    Exposure Preview: ...

</button>


<button
    id="shutterButton"
    class="control-button"
    onclick="captureImage()"
>

    SHUTTER

</button>


<div id="orientationStatus">

    Orientation: ...

</div>


<div id="focusStatus">

    Click image to focus

</div>


</div>


<script>


let currentOrientation = null;


/*
 * Focus position is stored as a percentage
 * of the actual visible image.
 *
 * This allows the focus box to remain
 * correctly positioned after browser resizing.
 */

let focusNormalX = null;
let focusNormalY = null;

/*
 * Used to ignore responses from older
 * focus requests when the user clicks
 * multiple times quickly.
 */

let focusRequestId = 0;


/* ----------------------------------------------
   ORIENTATION
   ---------------------------------------------- */

function updateOrientation(
    orientation
) {

    const status =
        document.getElementById(
            "orientationStatus"
        );


    let degrees = 0;


    if (
        orientation === 0
    ) {

        degrees = 0;

    } else if (
        orientation === 1
    ) {

        degrees = 270;

    } else if (
        orientation === 2
    ) {

        degrees = 90;

    } else if (
        orientation === 3
    ) {

        degrees = 180;
    }


    status.innerText =
        "Orientation: "
        +
        degrees
        +
        "°";


    if (
        currentOrientation ===
        orientation
    ) {

        return;
    }


    currentOrientation =
        orientation;


    const camera =
        document.getElementById(
            "camera"
        );


    if (
        orientation === 1 ||
        orientation === 2
    ) {

        camera.style.transform =

            orientation === 2

                ? "rotate(90deg)"

                : "rotate(-90deg)";

    } else if (
        orientation === 3
    ) {

        camera.style.transform =
            "rotate(180deg)";

    } else {

        camera.style.transform =
            "rotate(0deg)";
    }


    requestAnimationFrame(
        positionFocusBox
    );
}


function readOrientation() {

    fetch(
        "/orientation",
        {
            cache: "no-store"
        }
    )

    .then(
        response =>
            response.json()
    )

    .then(
        data => {

            if (
                data.success
            ) {

                updateOrientation(
                    data.orientation
                );
            }

        }
    )

    .catch(
        error => {

            console.error(
                "Orientation:",
                error
            );

        }
    );
}


/* ----------------------------------------------
   FOCUS BOX
   ---------------------------------------------- */

function positionFocusBox() {

    if (
        focusNormalX === null ||
        focusNormalY === null
    ) {

        return;
    }


    const camera =
        document.getElementById(
            "camera"
        );


    const focusBox =
        document.getElementById(
            "focusBox"
        );


    const rect =
        camera.getBoundingClientRect();


    const x =
        rect.left
        +
        (
            focusNormalX
            *
            rect.width
        );


    const y =
        rect.top
        +
        (
            focusNormalY
            *
            rect.height
        );


    focusBox.style.left =
        x + "px";


    focusBox.style.top =
        y + "px";


    focusBox.style.display =
        "block";
}


/* ----------------------------------------------
   CLICK TO FOCUS
   ---------------------------------------------- */

function clickToFocus(
    event
) {
    const requestId =
    ++focusRequestId;

    const camera =
        document.getElementById(
            "camera"
        );

    const rect =
        camera.getBoundingClientRect();


    /*
     * Coordinates of the click within the
     * displayed image.
     */

    let displayX =
        (
            event.clientX -
            rect.left
        ) / rect.width;

    let displayY =
        (
            event.clientY -
            rect.top
        ) / rect.height;


    if (
        displayX < 0 ||
        displayX > 1 ||
        displayY < 0 ||
        displayY > 1
    ) {

        return;
    }


    /*
     * Convert displayed coordinates back into
     * the ORIGINAL 640 x 480 live-view frame.
     */

    let normalX;
    let normalY;


    if (
        currentOrientation === 0
    ) {

        /*
         * 0° — LANDSCAPE
         *
         * This is the previously proven
         * working mapping.
         */

        normalX =
            displayX;

        normalY =
            displayY;


    } else if (
        currentOrientation === 1
    ) {

        /*
         * 270° — UPRIGHT PORTRAIT
         *
         * Reverse the portrait display rotation.
         */

        normalX =
            1.0 - displayY;

        normalY =
            displayX;


    } else if (
        currentOrientation === 2
    ) {

        /*
         * 90° — UPSIDE-DOWN PORTRAIT
         *
         * Inverse of the 270° transformation.
         */

        normalX =
            displayY;

        normalY =
            1.0 - displayX;


    } else if (
        currentOrientation === 3
    ) {

        /*
         * 180° — UPSIDE-DOWN LANDSCAPE
         */

        normalX =
            1.0 - displayX;

        normalY =
            1.0 - displayY;


    } else {

        /*
         * Safety fallback.
         */

        normalX =
            displayX;

        normalY =
            displayY;
    }


    /*
     * Keep the focus box visually at the point
     * where the user actually clicked.
     */

    focusNormalX =
        displayX;

    focusNormalY =
        displayY;


    positionFocusBox();


    const status =
        document.getElementById(
            "focusStatus"
        );


    status.innerText =
        "Focusing...";


    fetch(
        "/focus",
        {

            method:
                "POST",

            headers:
                {
                    "Content-Type":
                        "application/json"
                },

            body:
                JSON.stringify(
                    {
                        x:
                            normalX,

                        y:
                            normalY
                    }
                )
        }
    )

    .then(
        response =>
            response.json()
    )

    .then(
        data => {

            if (
                requestId !==
                focusRequestId
            ) {

                return;
            }

            if (
                data.success
            ) {

                status.innerText =

                    "Focus: "

                    +

                    data.sensor_x

                    +

                    ", "

                    +

                    data.sensor_y

                    +

                    " @ "

                    +

                    data.degrees

                    +

                    "°";

            } else {

                status.innerText =
                    "Focus error";

                console.error(
                    data.error
                );
            }

        }
    )

    .catch(
        error => {

            if (
                requestId !==
                focusRequestId
            ) {

                return;
            }

            status.innerText =
                "Focus error";

            console.error(
                error
            );

        }
    );
}


/* ----------------------------------------------
   EXPOSURE PREVIEW
   ---------------------------------------------- */

function updateExposureButton(
    enabled
) {

    const button =
        document.getElementById(
            "exposureButton"
        );


    if (
        enabled
    ) {

        button.innerText =
            "Exposure Preview: ON";

        button.classList.add(
            "enabled"
        );

    } else {

        button.innerText =
            "Exposure Preview: OFF";

        button.classList.remove(
            "enabled"
        );
    }
}


function readExposurePreviewState() {

    fetch(
        "/exposure-preview-state"
    )

    .then(
        response =>
            response.json()
    )

    .then(
        data => {

            if (
                data.success
            ) {

                updateExposureButton(
                    data.enabled
                );
            }

        }
    )

    .catch(
        error => {

            console.error(
                error
            );

        }
    );
}


function toggleExposurePreview() {

    const button =
        document.getElementById(
            "exposureButton"
        );


    button.disabled =
        true;


    fetch(
        "/toggle-exposure-preview",
        {
            method:
                "POST"
        }
    )

    .then(
        response =>
            response.json()
    )

    .then(
        data => {

            if (
                data.success
            ) {

                updateExposureButton(
                    data.enabled
                );

            } else {

                alert(
                    "Exposure Preview error: "
                    +
                    data.error
                );
            }

        }
    )

    .catch(
        error => {

            alert(
                "Camera control error: "
                +
                error
            );

        }
    )

    .finally(
        () => {

            button.disabled =
                false;

        }
    );
}


/* ----------------------------------------------
   SHUTTER
   ---------------------------------------------- */

function captureImage() {

    const button =
        document.getElementById(
            "shutterButton"
        );


    button.disabled =
        true;


    button.innerText =
        "CAPTURING...";


    fetch(
        "/capture",
        {
            method:
                "POST"
        }
    )

    .then(
        response =>
            response.json()
    )

    .then(
        data => {

            if (
                data.success
            ) {

                console.log(
                    "Image captured:",
                    data.folder
                    +
                    "/"
                    +
                    data.filename
                );

            } else {

                alert(
                    "Capture error: "
                    +
                    data.error
                );
            }

        }
    )

    .catch(
        error => {

            alert(
                "Camera capture error: "
                +
                error
            );

        }
    )

    .finally(
        () => {

            button.disabled =
                false;

            button.innerText =
                "SHUTTER";

        }
    );
}


/* ----------------------------------------------
   EVENT LISTENERS
   ---------------------------------------------- */

const cameraImage =
    document.getElementById(
        "camera"
    );


cameraImage.addEventListener(
    "click",
    clickToFocus
);


window.addEventListener(
    "resize",
    positionFocusBox
);


/* ----------------------------------------------
   INITIAL STATE
   ---------------------------------------------- */

readExposurePreviewState();

readOrientation();


setInterval(
    readOrientation,
    2000
);


</script>

</body>

</html>
"""


# ==================================================
# START SERVER
# ==================================================

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
