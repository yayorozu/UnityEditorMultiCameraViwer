using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

#if UNITY_2019_1_OR_NEWER
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
#endif

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
		private RenderTexture[] _previewTextures;
		private SceneView _sceneView;
#if UNITY_2019_1_OR_NEWER
		private PropertyInfo _customSceneProp;
#endif
		private Material _guiTextureBlit2SRGBMaterial;
		private Type _viewType;
		private float _positionSize;
		private Vector2 _scrollPosition;
		
		private const float PreviewSize = 0.4f;

		private void OnEnable()
		{
			SetSceneView();
			
#if UNITY_2019_1_OR_NEWER
			_customSceneProp = typeof(SceneView).GetProperty("customScene",
				BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
#endif
			
			_guiTextureBlit2SRGBMaterial = typeof(EditorGUIUtility)
				.GetProperty("GUITextureBlit2SRGBMaterial", BindingFlags.Static | BindingFlags.NonPublic)
				.GetValue(typeof(EditorGUIUtility), null) as Material;
			
			_previewCameras = new Camera[0];
			_previewTextures = new RenderTexture[0];
			
#if UNITY_2019_3_OR_NEWER
			var typeName = "PlayModeView";
#else
			var typeName = "GameView";
#endif
			_viewType = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a => a.GetTypes())
				.Where(t => t.Name == typeName)
				.Select(t => t).First();
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
			foreach (var texture in _previewTextures)
			{
				Destroy(texture);
			}
		}
		
		private void Update()
		{
			Repaint();
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

			_positionSize = 0;
			var cameras = Camera.allCameras;
			using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPosition))
			{
				_scrollPosition = scroll.scrollPosition;
				EditorGUILayout.BeginHorizontal();
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
					
					DrawPreviewCamera(i, Camera.allCameras[i], _previewCameras[i]);
				}
				EditorGUILayout.EndHorizontal();
			}
		}

		private void DrawPreviewCamera(int index, Camera camera, Camera previewCamera)
		{
			previewCamera.CopyFrom(camera);
			previewCamera.cameraType = CameraType.Preview;

#if UNITY_2019_1_OR_NEWER
			previewCamera.scene = (Scene) _customSceneProp.GetValue(_sceneView, new object[0]);
#endif

			var dstSkybox = previewCamera.GetComponent<Skybox>();
			if (dstSkybox)
			{
				var srcSkybox = camera.GetComponent<Skybox>();
				dstSkybox.enabled = srcSkybox && srcSkybox.enabled;
				if (dstSkybox.enabled)
					dstSkybox.material = srcSkybox.material;
			}

			var size = GetCameraSize(camera);
			var previewTexture = GetPreviewTextureWithSize(index, (int) size.x, (int) size.y);
			previewTexture.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
			previewCamera.targetTexture = previewTexture;
			previewCamera.pixelRect = new Rect(0, 0, size.x, size.y);
			
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
			
			if (_positionSize < size.x)
			{
				EditorGUILayout.EndHorizontal();
				_positionSize = position.width - size.x;
				EditorGUILayout.BeginHorizontal();
			}
			else
			{
				_positionSize -= size.x + 20;
			}
			using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(size.x)))
			{
				if (GUILayout.Button(camera.name, EditorStyles.label))
					Selection.activeObject = camera.gameObject;
				
				previewCamera.Render();

				var cameraRect = GUILayoutUtility.GetRect(size.x, size.y);
				Graphics.DrawTexture(
					new Rect(cameraRect.x, cameraRect.y, size.x, size.y),
					previewTexture,
					new Rect(0, 0, 1, 1), 0, 0, 0, 0,
					GUI.color,
					_guiTextureBlit2SRGBMaterial);
			}
		}

		private RenderTexture GetPreviewTextureWithSize(int index, int width, int height)
		{
			if (_previewTextures.Length <= index)
			{
				ArrayUtility.Add(ref _previewTextures, CreateRenderTexture(width, height));
			}
			else if (_previewTextures[index].width != width || _previewTextures[index].height != height)
			{
				Destroy(_previewTextures[index]);
				_previewTextures[index] = CreateRenderTexture(width, height);
			}

			return _previewTextures[index];
		}

		private RenderTexture CreateRenderTexture(int width, int height)
		{
#if UNITY_2019_1_OR_NEWER
			return new RenderTexture(width, height, 24, SystemInfo.GetGraphicsFormat(DefaultFormat.LDR));
#else
			return new RenderTexture(width, height, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
#endif
		}

		private Vector2 GetCameraSize(Camera camera)
		{
			var previewSize = camera.targetTexture != null
				? new Vector2(camera.targetTexture.width, camera.targetTexture.height)
#if UNITY_2019_3_OR_NEWER
				: (Vector2) _viewType.InvokeMember(
					"GetMainPlayModeViewTargetSize",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod, null, null,
					new object[0]);
#else
				: (Vector2) _viewType.InvokeMember(
					"GetMainGameViewTargetSize",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod, null, null,
					new object[0]);
#endif
			
			if (previewSize.x < 0f)
			{
				previewSize.x = _sceneView.position.width;
				previewSize.y = _sceneView.position.height;
			}
			
			Rect normalizedViewPortRect = camera.rect;
			// clamp normalized rect in [0,1]
			normalizedViewPortRect.xMin = Math.Max(normalizedViewPortRect.xMin, 0f);
			normalizedViewPortRect.yMin = Math.Max(normalizedViewPortRect.yMin, 0f);
			normalizedViewPortRect.xMax = Math.Min(normalizedViewPortRect.xMax, 1f);
			normalizedViewPortRect.yMax = Math.Min(normalizedViewPortRect.yMax, 1f);

			previewSize.x *= Mathf.Max(normalizedViewPortRect.width, 0f);
			previewSize.y *= Mathf.Max(normalizedViewPortRect.height, 0f);
			
			if (previewSize.x < 1f || previewSize.y < 1f)
				return Vector2.zero;

			var aspect = previewSize.x / previewSize.y;
			
			// 大きすぎるので サイズ調整
			previewSize.y = PreviewSize * previewSize.y;
			previewSize.x = previewSize.y * aspect;
			if (previewSize.y > _sceneView.position.height * 0.5f)
			{
				previewSize.y = _sceneView.position.height * 0.5f;
				previewSize.x = previewSize.y * aspect;
			}
			if (previewSize.x > _sceneView.position.width * 0.5f)
			{
				previewSize.x = _sceneView.position.width * 0.5f;
				previewSize.y = previewSize.x / aspect;
			}

			return previewSize;
		}
	}
}