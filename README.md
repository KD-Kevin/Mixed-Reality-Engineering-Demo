# Mixed Reality Engineering Demo

FridgeDemo detects a refrigerator in the camera feed and brings an interactive 3D hologram into the center of the view. Open the freezer, refrigerator compartment, or rear cover to inspect an exploded view and see the parts’ diagram references. Use camera-tracked hand gestures, Android touch, or a mouse in Unity.

**Demo video coming to YouTube.**

## How to use

- **Android** — Build an ARM64 APK from Unity’s **File → Build Profiles**, or install a copy of the APK. Allow camera access and aim the rear camera at a refrigerator.
- **PC in Unity** — Open the project in **Unity 6.3.14f1**, open [FridgeDemo](Assets/Demo/FridgeDemo.unity), and press **Play**. Use a webcam for detection and hand tracking. For an Android build, install **Android Build Support** with the SDK, NDK, and OpenJDK through Unity Hub.

Select the outlined refrigerator, or choose **Test Hologram** to start without a detected refrigerator. Select a door or the rear cover to expand that section. Aim at a part to see its label and reference number.

## Hand controls

<img src="docs/images/pistol-gesture.png" width="520" alt="Pistol gesture in two panels. Arm with the middle finger extended, then select by bending its middle joint while keeping the index finger straight.">

| Gesture | Action |
| --- | --- |
| **Pistol** | Extend the index finger, raise the thumb, and curl the ring and pinky fingers. Aim with the index finger. Lift the middle finger to arm, then bend its middle joint to select. Lift it again before selecting again. |
| **Fist**<br><img src="docs/images/fist-gesture.png" width="120" alt="Closed fist with four curled fingers and thumb folded across them"> | Close your hand and turn your wrist to rotate the hologram. Open your hand to release it, then make another fist to continue from the current angle. |
| **Three fingers**<br><img src="docs/images/three-fingers-gesture.png" width="120" alt="Palm facing forward with the index, middle, and ring fingers extended and the pinky folded"> | Extend the index, middle, and ring fingers and fold the pinky. Hold for **0.2 seconds** to open the test hologram or **0.3 seconds** to match a visible hologram to your hand’s orientation. |
| **Middle finger**<br><img src="docs/images/middle-finger-gesture.png" width="120" alt="Palm facing forward with only the middle finger extended"> | Hold the middle finger up for **0.2 seconds** with the index, ring, and pinky folded to leave an expanded section or close the hologram. Either the palm or the back of the hand can face the camera. |

## Touch and PC controls

| Action | Android touch | PC in Unity |
| --- | --- | --- |
| Select the detected fridge | Tap inside the outline | Left-click inside the outline |
| Open or close a section | Tap its door or rear cover | Left-click its door or rear cover |
| See a part’s reference | Touch and hold the part | Hover the pointer over the part |
| Rotate the hologram | Start with two fingers over the hologram and drag together | Left-click and drag the hologram |
| Go back one level | Tap **Back to Fridge** from a section | Press **Esc** to leave a section or close the hologram |
| Adjust the tracked wrist’s virtual depth | — | **Up / Down Arrow** |

On both platforms, use **Reset Rotation**, **Back to Fridge**, **Close Hologram**, **Test Hologram**, and **Show Helper / Hide Helper** on screen. **Close Demo** exits the Android app or stops Play Mode in Unity.

## References

- LG Electronics. *LTCS20020S Refrigerator Service Manual* (2020), §10, “Exploded View.” The hologram uses the **LG LTCS20020S** as a comparable model. Part labels show approximate diagram callouts. [LG service manual](https://device.report/manuals/lg-refrigerator-ltcs20020s-service-manual) · [Sears PartsDirect diagrams for LTCS20020S/00](https://www.searspartsdirect.com/model/52lk1pgefc-003204/lg-ltcs20020s-00-top-mount-refrigerator-parts)
- Google AI Edge. *MediaPipe Hand Landmarker*. The demo uses its hand landmarks for gesture recognition. [Hand Landmarker documentation](https://developers.google.com/edge/mediapipe/solutions/vision/hand_landmarker)
- Homuler. *MediaPipe Unity Plugin*, v0.16.3. The Unity integration for MediaPipe Tasks. [Versioned release](https://github.com/homuler/MediaPipeUnityPlugin/releases/tag/v0.16.3)
