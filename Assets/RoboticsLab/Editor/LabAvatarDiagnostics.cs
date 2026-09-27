using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Read-only avatar measurements. Edit-mode animation samples use an isolated clone.</summary>
    public static class LabAvatarDiagnostics
    {
        const string IdlePath = "Assets/RoboticsLab/Generated/TraineeIdle.anim";
        const string OutputPath = "Documentation/AvatarSnapshot.txt";
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static IEnumerator<int> regression;
        static List<string> regressionResults;

        [MenuItem("Robot Lab/Diagnostics/Validate Helmet And Walk")]
        public static void ValidateHelmetAndWalk()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start a fresh Play session before this regression.");
            if (regression != null) throw new InvalidOperationException("Avatar regression is already running.");
            var app = Object.FindObjectOfType<LabTrainingApp>();
            if (!app || !app.player || !app.player.equipment) throw new InvalidOperationException("Training app / player / equipment is missing.");
            if (app.HelmetWorn || app.player.equipment.IsEquipped || app.player.equipment.IsEquipping)
                throw new InvalidOperationException("This regression starts from the unequipped state. Exit and re-enter Play Mode first.");
            regressionResults = new List<string> { "UTC=" + DateTime.UtcNow.ToString("O", Invariant), "Play-mode helmet sequence and isolated sampled-walk checks." };
            regression = HelmetChecks(app);
            EditorApplication.update -= TickRegression;
            EditorApplication.update += TickRegression;
            Debug.Log("ROBOT_AVATAR_REGRESSION_STARTED");
        }

        static void TickRegression()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode stopped before the avatar regression finished.");
                if (regression != null && regression.MoveNext()) return;
            }
            catch (Exception e)
            {
                regressionResults.Add("FAIL: " + e.Message);
                Debug.LogException(e);
            }
            EditorApplication.update -= TickRegression;
            if (regression != null) regression.Dispose();
            regression = null;
            Directory.CreateDirectory("Documentation");
            File.WriteAllLines("Documentation/AvatarValidation.txt", regressionResults, new UTF8Encoding(false));
            Debug.Log("ROBOT_AVATAR_REGRESSION_FINISHED " + string.Join(" | ", regressionResults));
        }

        static IEnumerator<int> HelmetChecks(LabTrainingApp app)
        {
            LabThirdPerson player = app.player;
            LabHelmetEquipment equipment = player.equipment;
            app.ClosePanel();
            Vector3 pickupPosition=app.helmetPickup.position;
            player.Teleport(new Vector3(pickupPosition.x-.85f,.08f,pickupPosition.z),90);
            Vector3 shelfSize=HelmetWorldDimensions(app.helmetPickup);
            Vector3 wornSize=HelmetWorldDimensions(player.wornHelmet.transform);
            Check(Vector3.Distance(shelfSize,wornSize)/wornSize.magnitude<.002f,
                "Shelf and fitted helmet have matching physical dimensions: shelf="+V(shelfSize)+", fitted="+V(wornSize));
            var rack=app.helmetPickup.parent;
            var tray=rack.Find("HelmetSupportTray");
            foreach(Transform helmet in rack)
            {
                if(helmet.name!="HelmetPickup"&&!helmet.name.StartsWith("SpareHelmet",StringComparison.Ordinal))continue;
                Check(Vector3.Distance(HelmetWorldDimensions(helmet),wornSize)/wornSize.magnitude<.002f,
                    helmet.name+" matches the fitted helmet size");
                if(tray)Check(Mathf.Abs(RenderBounds(helmet).min.y-tray.GetComponent<Renderer>().bounds.max.y)<.002f,
                    helmet.name+" remains seated on the original rack tray");
            }
            CaptureShelfHelmet(app.helmetPickup);
            float maximumSizeError=0;
            bool handoffObserved=false;
            float start = Time.time;
            double deadline = EditorApplication.timeSinceStartup + 20;
            app.EquipHelmet();
            Check(equipment.IsEquipping && !equipment.IsEquipped, "EquipHelmet starts an asynchronous wearing sequence");
            Check(!app.HelmetWorn, "HelmetWorn remains false immediately after starting the sequence");
            app.OpenLesson();
            Check(app.panel != "lesson", "The lesson remains locked while the helmet is being fitted");
            float[] times = { .7f, 1.45f, 2.1f, 3.25f };
            string[] shots = { "HelmetReach", "HelmetRaised", "HelmetSeated", "HelmetFitted" };
            int shotIndex = 0;
            bool repeated = false;
            float completionObserved = -1;
            while (Time.time - start < equipment.duration + .32f || shotIndex < times.Length)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("The wearing sequence did not advance in Play Mode.");
                float elapsed = Time.time - start;
                if(player.wornHelmet.activeInHierarchy)
                {
                    maximumSizeError=Mathf.Max(maximumSizeError,Vector3.Distance(HelmetWorldDimensions(player.wornHelmet.transform),shelfSize)/shelfSize.magnitude);
                    handoffObserved|=!app.helmetPickup.gameObject.activeSelf;
                }
                if (!repeated && elapsed >= .35f)
                {
                    float before = equipment.EquipProgress;
                    app.EquipHelmet();
                    Check(before > 0 && Mathf.Abs(before - equipment.EquipProgress) < .0001f,
                        "Repeated EquipHelmet does not restart or reset the in-progress sequence");
                    Check(!app.HelmetWorn && player.IsEquippingHelmet && !player.inputEnabled,
                        "The sequence keeps teaching locked and disables roaming input");
                    repeated = true;
                }
                if (completionObserved < 0 && equipment.IsEquipped) completionObserved = elapsed;
                if (shotIndex < times.Length && elapsed >= times[shotIndex])
                {
                    CaptureLiveAvatar(player, shots[shotIndex], false);
                    regressionResults.Add("CAPTURE: " + shots[shotIndex] + " observedGameSeconds=" + F(elapsed)
                        + " progress=" + F(equipment.EquipProgress) + " HelmetWorn=" + app.HelmetWorn);
                    shotIndex++;
                }
                yield return 0;
            }
            Check(repeated, "Duplicate interaction was checked during the animation");
            Check(handoffObserved&&maximumSizeError<.002f,
                "Shelf-to-hands-to-head keeps the same physical size across rendered frames; largest relative dimension error="+F(maximumSizeError));
            Check(app.HelmetWorn && equipment.IsEquipped && !equipment.IsEquipping,
                "The completed sequence sets HelmetWorn and IsEquipped together");
            Check(completionObserved >= equipment.duration - .12f && completionObserved <= equipment.duration + .4f,
                "Completion observed at " + F(completionObserved) + " game seconds for configured duration " + F(equipment.duration));
            VerifyFitted(player, "Completed wearing sequence");
            var snapshot = new StringBuilder();
            Capture(snapshot, player, "LIVE_FITTED_WITH_CPU_SKINNING");
            File.WriteAllText("Documentation/AvatarFittedSnapshot.txt", snapshot.ToString(), new UTF8Encoding(false));
            player.Teleport(new Vector3(0,.075f,2.1f),0);
            using (var walk = SampleWalkAndCapture(player))
                while (walk.MoveNext()) yield return walk.Current;
            regressionResults.Add("NOTE: Walk phases are captured on the live Play-mode avatar after three rendered frame intervals. This verifies displayed pose variation and head-relative helmet stability, not foot-slip under live navigation.");
        }

        static void VerifyFitted(LabThirdPerson player, string context)
        {
            var equipment = player.equipment;
            var helmet = player.wornHelmet.transform;
            float positionError = Vector3.Distance(helmet.localPosition, equipment.fittedLocalPosition);
            float rotationError = Quaternion.Angle(helmet.localRotation, equipment.fittedLocalRotation);
            float scaleError = Vector3.Distance(helmet.localScale, equipment.fittedLocalScale);
            Check(helmet.parent == player.head && positionError < .0005f && rotationError < .1f && scaleError < .0005f,
                context + " matches fitted head-local pose: position=" + F(positionError)
                + ", rotationDegrees=" + F(rotationError) + ", scale=" + F(scaleError));
        }

        static Vector3 HelmetWorldDimensions(Transform helmet)
        {
            // Compare actual mesh extents in the helmet's orientation, so turning/tilting the
            // hat cannot masquerade as a size change in a world-axis-aligned renderer bound.
            Bounds bounds=new Bounds();bool first=true;
            Quaternion inverse=Quaternion.Inverse(helmet.rotation);
            foreach(var filter in helmet.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!filter.sharedMesh)continue;
                var b=filter.sharedMesh.bounds;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                {
                    var p=inverse*(filter.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z)))-helmet.position);
                    if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                }
            }
            return bounds.size;
        }

        static Bounds RenderBounds(Transform root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>(true);
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);return bounds;
        }

        [MenuItem("Robot Lab/Capture/Shelf Helmet")]
        public static void CaptureShelfHelmetInScene()
        {
            var pickup=GameObject.Find("HelmetPickup");
            if(!pickup)throw new InvalidOperationException("The shelf pickup must be visible.");
            CaptureShelfHelmet(pickup.transform);
        }

        static void CaptureShelfHelmet(Transform helmet)
        {
            var go=new GameObject("__ShelfHelmetDiagnosticCamera"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=34;camera.nearClipPlane=.03f;
                var center=RenderBounds(helmet).center;
                camera.transform.position=center+new Vector3(-.85f,.38f,.15f);camera.transform.LookAt(center);
                RenderToFile(camera,"Documentation/Screenshots/HelmetShelf.png");
            }
            finally{Object.DestroyImmediate(go);}
        }

        static void Check(bool passed, string detail)
        {
            regressionResults.Add((passed ? "PASS: " : "FAIL: ") + detail);
            if (!passed) throw new InvalidOperationException(detail);
        }

        static IEnumerator<int> SampleWalkAndCapture(LabThirdPerson source)
        {
            AnimationClip walk = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/RoboticsLab/Generated/TraineeNeutralWalk.anim");
            if (!walk && source.animator.runtimeAnimatorController)
                walk = source.animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c && c.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(walk != null, "A generated walking clip is available for quarter-cycle sampling");
            regressionResults.Add("WalkClip=" + AssetDatabase.GetAssetPath(walk) + " length=" + F(walk.length));
            Animator animator = source.animator;
            bool wasPlayerEnabled = source.enabled;
            bool wasAnimatorEnabled = animator.enabled;
            float previousSpeed = animator.speed;
            var snapshots = new StringBuilder(40000);
            try
            {
                // Keep the live Animator / renderer pair so GPU skinning updates across real frames.
                // Disabling the mover prevents UpdateAnimation from resetting the Speed parameter.
                source.enabled = false;
                animator.enabled = true;
                float[] phases = { 0, .25f, .5f, .75f };
                string[] names = { "WalkFrame00", "WalkFrame25", "WalkFrame50", "WalkFrame75" };
                Vector3 firstLeftFoot = Vector3.zero, firstRightFoot = Vector3.zero;
                float greatestFootVariation = 0;
                for (int i = 0; i < phases.Length; i++)
                {
                    animator.speed = 1;
                    animator.SetFloat("Speed", 1);
                    animator.Play("Walk", 0, phases[i]);
                    animator.Update(0);
                    animator.speed = 0;
                    int targetFrame = Time.frameCount + 3;
                    double deadline = EditorApplication.timeSinceStartup + 10;
                    while (Time.frameCount < targetFrame)
                    {
                        if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("No rendered Play frames advanced during walk capture.");
                        yield return 0;
                    }
                    VerifyFitted(source, "Walk phase " + F(phases[i]));
                    Capture(snapshots, source, "LIVE_WALK_PHASE_" + F(phases[i]));
                    int measuredVertices;
                    string lowestMesh;
                    float bodyMinimum = CpuBodyLowestY(source, out measuredVertices, out lowestMesh);
                    regressionResults.Add("Walk phase " + F(phases[i]) + " CPU_SKINNED_BODY_MIN_Y=" + F(bodyMinimum)
                        + " PLAYER_ROOT_Y=" + F(source.transform.position.y)
                        + " MINUS_ROOT_Y=" + F(bodyMinimum - source.transform.position.y)
                        + " measuredVertices=" + measuredVertices + " lowestMesh=" + lowestMesh);
                    var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    if (left && right)
                    {
                        Vector3 l = source.visual.InverseTransformPoint(left.position), r = source.visual.InverseTransformPoint(right.position);
                        if (i == 0) { firstLeftFoot = l; firstRightFoot = r; }
                        else greatestFootVariation = Mathf.Max(greatestFootVariation, Vector3.Distance(l, firstLeftFoot), Vector3.Distance(r, firstRightFoot));
                        regressionResults.Add("Walk phase " + F(phases[i]) + " frame=" + Time.frameCount
                            + " stateNormalizedTime=" + F(animator.GetCurrentAnimatorStateInfo(0).normalizedTime)
                            + " footPositionsInVisual: left=" + V(l) + " right=" + V(r));
                    }
                    CaptureLiveAvatar(source, names[i], true);
                }
                Check(greatestFootVariation > .02f, "Quarter-cycle walking samples change the leg pose; largest foot displacement=" + F(greatestFootVariation));
                File.WriteAllText("Documentation/AvatarWalkSnapshots.txt", snapshots.ToString(), new UTF8Encoding(false));
            }
            finally
            {
                if (animator)
                {
                    animator.speed = previousSpeed;
                    animator.SetFloat("Speed", 0);
                    animator.Play("Idle", 0, 0);
                    animator.Update(0);
                    animator.enabled = wasAnimatorEnabled;
                }
                if (source) source.enabled = wasPlayerEnabled;
            }
        }

        static void CaptureLiveAvatar(LabThirdPerson player, string name, bool fullBody)
        {
            var go = new GameObject("__AvatarDiagnosticCamera") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.nearClipPlane = .03f;
                camera.farClipPlane = 60;
                camera.fieldOfView = 34;
                camera.clearFlags = CameraClearFlags.Skybox;
                PositionAvatarCamera(camera, player, fullBody);
                RenderToFile(camera, "Documentation/Screenshots/" + name + ".png");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void PositionAvatarCamera(Camera camera, LabThirdPerson player, bool fullBody)
        {
            Vector3 target = player.transform.position + Vector3.up * (fullBody ? .96f : 1.37f);
            Vector3 forward = player.visual ? player.visual.forward : player.transform.forward;
            Vector3 right = player.visual ? player.visual.right : player.transform.right;
            camera.transform.position = target + forward * (fullBody ? 3.15f : 2.25f)
                + right * (fullBody ? 1.7f : 1.25f) + Vector3.up * (fullBody ? .35f : .16f);
            if(player.transform.position.x>5.5f)
                camera.transform.position=target+forward*.65f-right*(fullBody?2.9f:2.1f)+Vector3.up*(fullBody?.3f:.15f);
            camera.transform.LookAt(target);
        }

        static void RenderToFile(Camera camera, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            var render = new RenderTexture(1440, 1440, 24) { antiAliasing = 4 };
            var texture = new Texture2D(1440, 1440, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                texture.ReadPixels(new Rect(0, 0, 1440, 1440), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = previousTarget;
                render.Release();
                Object.DestroyImmediate(render);
                Object.DestroyImmediate(texture);
            }
        }

        [MenuItem("Robot Lab/Diagnostics/Inspect Avatar")]
        public static void InspectAvatar()
        {
            var player = Object.FindObjectsOfType<LabThirdPerson>(true)
                .FirstOrDefault(p => p.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(p.gameObject.scene));
            if (!player) throw new InvalidOperationException("No LabThirdPerson exists in the loaded scene.");
            Scene originalScene = player.gameObject.scene;
            bool wasDirty = originalScene.isDirty;
            var text = new StringBuilder(24000);
            text.AppendLine("ROBOT LAB AVATAR SNAPSHOT");
            text.AppendLine("UTC=" + DateTime.UtcNow.ToString("O", Invariant));
            text.AppendLine("Unity=" + Application.unityVersion + " playMode=" + EditorApplication.isPlaying);
            text.AppendLine("Scene=" + originalScene.path + " sceneDirtyBefore=" + wasDirty);
            text.AppendLine("Units are scene-space units, not calibrated real-device dimensions.");
            text.AppendLine("Inactive Renderer.bounds may be cached. CPU skinning (bone.localToWorldMatrix * bindpose * sourceVertex) is the primary surface measurement; BakeMesh is a scale-sensitive comparison.");
            Capture(text, player, EditorApplication.isPlaying ? "LIVE_PLAY_POSE" : "CURRENT_EDIT_POSE");

            if (!EditorApplication.isPlaying)
            {
                AnimationClip idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
                if (!idle) text.AppendLine("IDLE_SAMPLE_SKIPPED: missing " + IdlePath);
                else SampleIdleClone(text, player, idle);
            }

            text.AppendLine("SceneDirtyAfter=" + originalScene.isDirty + " unchanged=" + (wasDirty == originalScene.isDirty));
            Directory.CreateDirectory("Documentation");
            File.WriteAllText(OutputPath, text.ToString(), new UTF8Encoding(false));
            Debug.Log("ROBOT_LAB_AVATAR_SNAPSHOT_READY " + Path.GetFullPath(OutputPath));
        }

        static void SampleIdleClone(StringBuilder text, LabThirdPerson source, AnimationClip idle)
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            GameObject clone = null;
            try
            {
                clone = Object.Instantiate(source.gameObject);
                clone.name = "__AvatarDiagnosticPreview";
                SceneManager.MoveGameObjectToScene(clone, previewScene);
                clone.hideFlags = HideFlags.HideAndDontSave;
                foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var camera in clone.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                var preview = clone.GetComponent<LabThirdPerson>();
                var animator = preview.animator ? preview.animator : clone.GetComponentInChildren<Animator>(true);
                if (!animator)
                {
                    text.AppendLine("IDLE_SAMPLE_SKIPPED: clone has no Animator.");
                    return;
                }
                animator.enabled = false;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                text.AppendLine("IdleClip=" + AssetDatabase.GetAssetPath(idle) + " length=" + F(idle.length) + " humanMotion=" + idle.humanMotion);
                // SampleAnimation touches only the hidden clone; no AnimationMode or scene state is changed.
                idle.SampleAnimation(animator.gameObject, 0f);
                Capture(text, preview, "PREVIEW_IDLE_AT_0_SECONDS");
                idle.SampleAnimation(animator.gameObject, Mathf.Min(.5f, idle.length));
                Capture(text, preview, "PREVIEW_IDLE_AT_HALF_SECOND");
            }
            catch (Exception e)
            {
                text.AppendLine("IDLE_SAMPLE_ERROR: " + e);
            }
            finally
            {
                if (clone) Object.DestroyImmediate(clone);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        static void Capture(StringBuilder text, LabThirdPerson player, string label)
        {
            text.AppendLine();
            text.AppendLine("===== " + label + " =====");
            WriteTransform(text, "PLAYER_ROOT", player.transform);
            WriteTransform(text, "VISUAL_ROOT", player.visual);
            var controller = player.GetComponent<CharacterController>();
            if (controller)
                text.AppendLine("CharacterController: enabled=" + controller.enabled + " height=" + F(controller.height)
                    + " radius=" + F(controller.radius) + " center=" + V(controller.center) + " stepOffset=" + F(controller.stepOffset));
            Animator animator = player.animator ? player.animator : player.GetComponentInChildren<Animator>(true);
            Transform mappedHead = null;
            if (animator)
            {
                WriteTransform(text, "ANIMATOR_ROOT", animator.transform);
                bool human = animator.avatar && animator.avatar.isValid && animator.avatar.isHuman;
                text.AppendLine("Animator: enabled=" + animator.enabled + " initialized=" + animator.isInitialized
                    + " avatar=" + (animator.avatar ? animator.avatar.name : "<null>") + " humanoid=" + human
                    + " humanScale=" + F(animator.humanScale) + " applyRootMotion=" + animator.applyRootMotion
                    + " speed=" + F(animator.speed) + " culling=" + animator.cullingMode);
                text.AppendLine("Controller=" + (animator.runtimeAnimatorController ? AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) : "<null>"));
                if (human)
                {
                    foreach (HumanBodyBones id in Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (id == HumanBodyBones.LastBone) continue;
                        Transform bone = animator.GetBoneTransform(id);
                        if (bone) WriteTransform(text, "HUMANOID_BONE " + id, bone);
                    }
                    mappedHead = animator.GetBoneTransform(HumanBodyBones.Head);
                }
                else text.AppendLine("HUMANOID_MAPPING_UNAVAILABLE");
            }
            Transform head = mappedHead ? mappedHead : player.head;
            WriteTransform(text, "CONFIGURED_HEAD", player.head);
            text.AppendLine("ConfiguredHeadMatchesHumanoid=" + (player.head == mappedHead));
            Transform renderRoot = player.visual ? player.visual : player.transform;
            text.AppendLine("--- RENDERER BOUNDS ---");
            foreach (Renderer renderer in renderRoot.GetComponentsInChildren<Renderer>(true))
            {
                text.AppendLine("Renderer=" + Hierarchy(renderer.transform) + " type=" + renderer.GetType().Name
                    + " activeSelf=" + renderer.gameObject.activeSelf + " activeInHierarchy=" + renderer.gameObject.activeInHierarchy
                    + " enabled=" + renderer.enabled + " cachedWorldBounds=" + B(renderer.bounds));
                var skinned = renderer as SkinnedMeshRenderer;
                if (skinned)
                    text.AppendLine("  skinnedLocalBounds=" + B(skinned.localBounds) + " rootBone="
                        + (skinned.rootBone ? Hierarchy(skinned.rootBone) : "<null>") + " updateWhenOffscreen=" + skinned.updateWhenOffscreen);
            }

            Bounds headWorld, headLocal;
            int selected = MeasureHeadVertices(text, renderRoot, head, out headWorld, out headLocal);
            text.AppendLine("--- HELMET ---");
            GameObject helmet = player.wornHelmet;
            if (!helmet)
            {
                text.AppendLine("WORN_HELMET=<null>");
                return;
            }
            text.AppendLine("HelmetActiveSelf=" + helmet.activeSelf + " activeInHierarchy=" + helmet.activeInHierarchy);
            WriteTransform(text, "HELMET_ROOT", helmet.transform);
            WriteTransform(text, "HELMET_PARENT_ANCHOR", helmet.transform.parent);
            if (head)
            {
                text.AppendLine("HelmetRootInHeadSpace=" + V(head.InverseTransformPoint(helmet.transform.position)));
                text.AppendLine("HelmetRootRotationRelativeToHead=" + Q(Quaternion.Inverse(head.rotation) * helmet.transform.rotation));
                text.AppendLine("PlayerRootInHeadSpace=" + V(head.InverseTransformPoint(player.transform.position)));
            }
            bool haveHelmetBounds = false;
            Bounds allHelmet = new Bounds();
            foreach (var filter in helmet.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                Bounds worldBounds = TransformBounds(filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
                text.AppendLine("HelmetMesh=" + Hierarchy(filter.transform) + " asset=" + AssetDatabase.GetAssetPath(filter.sharedMesh)
                    + " vertices=" + filter.sharedMesh.vertexCount + " meshLocalBounds=" + B(filter.sharedMesh.bounds)
                    + " transformedWorldAABB=" + B(worldBounds));
                WriteTransform(text, "  HELMET_MESH_TRANSFORM", filter.transform);
                if (head)
                    text.AppendLine("  meshBoundsInHeadSpace=" + B(TransformBounds(head.worldToLocalMatrix * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds)));
                if (!haveHelmetBounds) { allHelmet = worldBounds; haveHelmetBounds = true; }
                else allHelmet.Encapsulate(worldBounds);
            }
            if (haveHelmetBounds)
            {
                text.AppendLine("HelmetCombinedMeshWorldBounds=" + B(allHelmet));
                if (selected > 0)
                {
                    text.AppendLine("HelmetMinYMinusHeadWeightedMaxY=" + F(allHelmet.min.y - headWorld.max.y)
                        + " (positive means a vertical gap above the weighted head surface)");
                    text.AppendLine("HelmetCenterXZMinusHeadCenterXZ=" + V(new Vector3(allHelmet.center.x - headWorld.center.x, 0, allHelmet.center.z - headWorld.center.z)));
                }
            }
        }

        static int MeasureHeadVertices(StringBuilder text, Transform renderRoot, Transform head, out Bounds totalWorld, out Bounds totalHead)
        {
            totalWorld = new Bounds(); totalHead = new Bounds();
            text.AppendLine("--- HEAD VERTICES (exact mapped Head bone weight > 0.5): CPU skinning primary, BakeMesh comparison ---");
            if (!head) { text.AppendLine("No mapped/configured Head bone."); return 0; }
            int total = 0;
            foreach (var renderer in renderRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.sharedMesh) continue;
                var bones = renderer.bones;
                int headIndex = Array.IndexOf(bones, head);
                if (headIndex < 0) { text.AppendLine("NoHeadInfluence: " + Hierarchy(renderer.transform)); continue; }
                Mesh baked = new Mesh { name = "AvatarDiagnosticBake", hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    // useScale=false keeps the vertices in renderer local coordinates; TransformPoint applies its scale once.
                    renderer.BakeMesh(baked, false);
                    var vertices = baked.vertices;
                    var weights = renderer.sharedMesh.boneWeights;
                    var original = renderer.sharedMesh.vertices;
                    var bindposes = renderer.sharedMesh.bindposes;
                    Matrix4x4[] skinMatrices = new Matrix4x4[Math.Min(bones.Length, bindposes.Length)];
                    for (int i = 0; i < skinMatrices.Length; i++) skinMatrices[i] = bones[i] ? bones[i].localToWorldMatrix * bindposes[i] : Matrix4x4.identity;
                    int count = Math.Min(original.Length, Math.Min(vertices.Length, weights.Length)), localCount = 0;
                    Bounds world = new Bounds(), local = new Bounds(), bakedWorld = new Bounds();
                    float largestDifference = 0, sumDifference = 0;
                    for (int i = 0; i < count; i++)
                    {
                        BoneWeight weight = weights[i];
                        float influence = (weight.boneIndex0 == headIndex ? weight.weight0 : 0)
                            + (weight.boneIndex1 == headIndex ? weight.weight1 : 0)
                            + (weight.boneIndex2 == headIndex ? weight.weight2 : 0)
                            + (weight.boneIndex3 == headIndex ? weight.weight3 : 0);
                        if (influence <= .5f) continue;
                        Vector3 p = SkinPoint(original[i], weight, skinMatrices);
                        Vector3 bakePoint = renderer.transform.TransformPoint(vertices[i]);
                        float difference = Vector3.Distance(p, bakePoint);
                        largestDifference = Mathf.Max(largestDifference, difference); sumDifference += difference;
                        Vector3 inHead = head.InverseTransformPoint(p);
                        if (localCount++ == 0) { world = new Bounds(p, Vector3.zero); local = new Bounds(inHead, Vector3.zero); bakedWorld = new Bounds(bakePoint, Vector3.zero); }
                        else { world.Encapsulate(p); local.Encapsulate(inHead); bakedWorld.Encapsulate(bakePoint); }
                        if (total++ == 0) { totalWorld = new Bounds(p, Vector3.zero); totalHead = new Bounds(inHead, Vector3.zero); }
                        else { totalWorld.Encapsulate(p); totalHead.Encapsulate(inHead); }
                    }
                    text.AppendLine("HeadSkin=" + Hierarchy(renderer.transform) + " headBoneIndex=" + headIndex
                        + " meshVertices=" + vertices.Length + " boneWeightRecords=" + weights.Length + " selectedVertices=" + localCount);
                    if (localCount > 0)
                    {
                        text.AppendLine("  CPU_WORLD_BOUNDS=" + B(world) + " CPU_HEAD_LOCAL_BOUNDS=" + B(local));
                        text.AppendLine("  BAKE_FALSE_THEN_TRANSFORM_WORLD_BOUNDS=" + B(bakedWorld));
                        text.AppendLine("  CpuVsBakeWorldMaxDifference=" + F(largestDifference) + " meanDifference=" + F(sumDifference/localCount));
                    }
                    if (vertices.Length != weights.Length) text.AppendLine("  WARNING: vertex/weight counts differ; only their shared range was measured.");
                }
                catch (Exception e) { text.AppendLine("HeadSkinError=" + Hierarchy(renderer.transform) + " " + e.Message); }
                finally { Object.DestroyImmediate(baked); }
            }
            text.AppendLine("HeadSelectedVerticesTotal=" + total + " source=Mesh.boneWeights (four-influence mesh view)");
            if (total > 0)
            {
                text.AppendLine("HeadCombinedWorldBounds_CPU=" + B(totalWorld));
                text.AppendLine("HeadCombinedLocalBounds=" + B(totalHead));
                text.AppendLine("HeadWorldTop=" + V(new Vector3(totalWorld.center.x, totalWorld.max.y, totalWorld.center.z)));
            }
            return total;
        }

        static Vector3 SkinPoint(Vector3 point, BoneWeight weight, Matrix4x4[] matrices)
        {
            Vector3 result = Vector3.zero;
            if (weight.weight0 > 0 && weight.boneIndex0 < matrices.Length) result += matrices[weight.boneIndex0].MultiplyPoint3x4(point) * weight.weight0;
            if (weight.weight1 > 0 && weight.boneIndex1 < matrices.Length) result += matrices[weight.boneIndex1].MultiplyPoint3x4(point) * weight.weight1;
            if (weight.weight2 > 0 && weight.boneIndex2 < matrices.Length) result += matrices[weight.boneIndex2].MultiplyPoint3x4(point) * weight.weight2;
            if (weight.weight3 > 0 && weight.boneIndex3 < matrices.Length) result += matrices[weight.boneIndex3].MultiplyPoint3x4(point) * weight.weight3;
            return result;
        }

        static float CpuBodyLowestY(LabThirdPerson player, out int measuredVertices, out string lowestMesh)
        {
            float lowest = float.PositiveInfinity;
            measuredVertices = 0; lowestMesh = "<none>";
            Transform root = player.visual ? player.visual : player.transform;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.sharedMesh) continue;
                var mesh = renderer.sharedMesh;
                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                var bones = renderer.bones;
                var bindposes = mesh.bindposes;
                Matrix4x4[] matrices = new Matrix4x4[Math.Min(bones.Length, bindposes.Length)];
                for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i] ? bones[i].localToWorldMatrix * bindposes[i] : Matrix4x4.identity;
                int count = Math.Min(vertices.Length, weights.Length);
                for (int i = 0; i < count; i++)
                {
                    float y = SkinPoint(vertices[i], weights[i], matrices).y;
                    measuredVertices++;
                    if (y < lowest) { lowest = y; lowestMesh = renderer.name; }
                }
            }
            return lowest;
        }

        static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
        {
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(bounds.min), Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z)));
            return result;
        }

        static void WriteTransform(StringBuilder text, string label, Transform transform)
        {
            if (!transform) { text.AppendLine(label + "=<null>"); return; }
            text.AppendLine(label + " path=" + Hierarchy(transform));
            text.AppendLine("  worldPosition=" + V(transform.position) + " worldRotation=" + Q(transform.rotation)
                + " worldEuler=" + V(transform.eulerAngles) + " lossyScale=" + V(transform.lossyScale));
            text.AppendLine("  localPosition=" + V(transform.localPosition) + " localRotation=" + Q(transform.localRotation)
                + " localEuler=" + V(transform.localEulerAngles) + " localScale=" + V(transform.localScale));
            text.AppendLine("  worldRight=" + V(transform.right) + " worldUp=" + V(transform.up) + " worldForward=" + V(transform.forward));
        }
        static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent) + "/" + t.name : t.name;
        static string F(float value) => value.ToString("F6", Invariant);
        static string V(Vector3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";
        static string Q(Quaternion q) => "(" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + ")";
        static string B(Bounds b) => "center=" + V(b.center) + " size=" + V(b.size) + " min=" + V(b.min) + " max=" + V(b.max);
    }
}
