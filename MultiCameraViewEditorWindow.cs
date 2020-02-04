using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

// https://github.com/Unity-Technologies/UnityCsReference/blob/e5f43177f856c5f5bfe8537c9ab6f92425fdcc3b/Editor/Mono/Inspector/CameraEditor.cs
namespace UniLib
{
	public class MultiCameraViewEditorWindow : EditorWindow
	{
		[MenuItem("Tools/MultiCameraViewer")]
		private static void ShowWindow()
		{
			var window = GetWindow<MultiCameraViewEditorWindow>();
			window.titleContent = new GUIContent("MultiCameraViewer");
			window.Show();
		}

		private Camera[] _previewCameras;
		private RenderTexture _previewTexture;
		private SceneView _sceneView;
		private PropertyInfo _customSceneProp;
		private Material _guiTextureBlit2SRGBMaterial;

		private void OnEnable()
		{
			SetSceneView();
			
			_customSceneProp = typeof(SceneView).GetProperty("customScene",
				BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
			
			_guiTextureBlit2SRGBMaterial = typeof(EditorGUIUtility)
				.GetProperty("GUITextureBlit2SRGBMaterial", BindingFlags.Static | BindingFlags.NonPublic)
				?.GetValue(typeof(EditorGUIUtility), null) as Material;
			
			_previewCameras = new Camera[0];
		}

		private void SetSceneView()
		{
			var findSceneView = Resources.FindObjectsOfTypeAll(typeof(SceneView));
			_sceneView = findSceneView.Length <= 0
				? GetWindow(typeof(SceneView)) as SceneView
				: findSceneView[0] as SceneView;

		}

		private void OnDestroy()
		{
			foreach (var camera in _previewCameras)
			{
				
				DestroyImmediate(camera.gameObject, true);
			}
		}

		private void OnGUI()
		{
			// 途中でSceneViewが破棄される可能性があるのでチェックする
			if (_sceneView == null)
			{
				SetSceneView();

				if (_sceneView == null)
					return;
			}
			
			if (GUILayout.Button("Repaint"))
				Repaint();

			var cameras = Camera.allCameras;
			for (var i = 0; i < cameras.Length; i++)
			{
				if (_previewCameras.Length <= i)
				{
					var pvc = EditorUtility.CreateGameObjectWithHideFlags(
						"Preview Camera",
						HideFlags.HideAndDontSave,
						typeof(Camera),
						typeof(Skybox)).GetComponent<Camera>();
					pvc.enabled = false;
					ArrayUtility.Add(ref _previewCameras, pvc); 
				}
				
				var camera = Camera.allCameras[i];
				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.ObjectField(camera.name, camera, typeof(Camera));
					DrawPreviewCamera(camera, _previewCameras[i]);
				}
			}
		}

		private void DrawPreviewCamera(Camera camera, Camera previewCamera)
		{
			if (Event.current.type != EventType.Repaint)
				return;

			previewCamera.CopyFrom(camera);
			previewCamera.cameraType = CameraType.Preview;

			previewCamera.scene = (Scene) _customSceneProp.GetValue(_sceneView);

			var dstSkybox = previewCamera.GetComponent<Skybox>();
			if (dstSkybox)
			{
				var srcSkybox = camera.GetComponent<Skybox>();
				if (srcSkybox && srcSkybox.enabled)
				{
					dstSkybox.enabled = true;
					dstSkybox.material = srcSkybox.material;
				}
				else
				{
					dstSkybox.enabled = false;
				}
			}

			var cameraRect = new Rect(0, 50, 256, 256);
			var previewTexture = GetPreviewTextureWithSize((int) cameraRect.width, (int) cameraRect.height);
			previewTexture.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
			previewCamera.targetTexture = previewTexture;
			previewCamera.pixelRect = new Rect(0, 0, cameraRect.width, cameraRect.width);

			typeof(Handles).InvokeMember("EmitGUIGeometryForCamera",
				BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod, null, null, new object[]
				{
					camera, previewCamera
				});

			if (camera.usePhysicalProperties)
			{
				// when sensor size is reduced, the previous frame is still visible behing so we need to clear the texture before rendering.
				var rt = RenderTexture.active;
				RenderTexture.active = previewTexture;
				GL.Clear(false, true, Color.clear);
				RenderTexture.active = rt;
			}

			previewCamera.Render();
			Graphics.DrawTexture(
				cameraRect,
				previewTexture,
				new Rect(0, 0, 1, 1), 0, 0, 0, 0,
				GUI.color,
				_guiTextureBlit2SRGBMaterial);
		}

		private RenderTexture GetPreviewTextureWithSize(int width, int height)
		{
			if (_previewTexture == null || _previewTexture.width != width || _previewTexture.height != height)
				_previewTexture = new RenderTexture(width, height, 24, SystemInfo.GetGraphicsFormat(DefaultFormat.LDR));

			return _previewTexture;
		}
	}
}