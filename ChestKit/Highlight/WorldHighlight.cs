using System;
using System.Collections.Generic;
using JoksterCube.ChestKit.Common;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace JoksterCube.ChestKit.Highlight;

// For a while, paints objects in solid, pulsing color, drawn on top of everything so they also show through walls
// (as ProtectSpawnerFarms shows spawners). Each object has a color, read every frame so config changes show at once.
// Also shows a see-through sphere around a point, with a status effect on the local player counting down its time.
public static class WorldHighlight
{
    private const string Feature = "World highlight";
    private const CameraEvent RenderEvent = CameraEvent.AfterForwardAlpha;
    private const float PulseSpeed = 3.5f;
    private const float MinAlpha = 0.55f;
    private const float MaxAlpha = 0.95f;
    private const float RendererRefreshInterval = 1f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly List<(GameObject Target, Func<Color> Color)> Targets = [];
    private static readonly List<(Renderer Renderer, Func<Color> Color)> Renderers = [];
    private static readonly List<Renderer> Found = [];
    // One material per color source.
    private static readonly Dictionary<Func<Color>, Material> Materials = [];

    private static float _until;
    private static float _nextRefresh;
    private static CommandBuffer? _buffer;
    private static Camera? _camera;
    private static Shader? _shader;
    private static bool _shaderMissing;
    private static bool _hooked;

    private static GameObject? _sphere;
    private static Material? _sphereMaterial;
    private static StatusEffect? _sphereEffect;

    // Whether objects or a sphere are shown right now.
    public static bool IsShowing => (Targets.Count > 0 && Time.unscaledTime < _until) || _sphere;

    // Whether a sphere is shown right now.
    public static bool IsShowingSphere => _sphere;

    // Stops showing objects and the sphere.
    public static void StopAll()
    {
        Stop();
        RemoveSphere();
    }

    // Replaces whatever is shown. Pass the same color function for objects that share a color.
    public static void Show(IEnumerable<(GameObject Target, Func<Color> Color)> targets, float seconds)
    {
        Stop();
        foreach (var target in targets)
            if (target.Target) Targets.Add(target);
        if (Targets.Count == 0) return;
        if (!_hooked)
        {
            Camera.onPreRender += OnPreRender;
            _hooked = true;
        }
        _until = Time.unscaledTime + seconds;
        _nextRefresh = 0f;
    }

    public static void Stop()
    {
        _until = 0f;
        Targets.Clear();
        Renderers.Clear();
        _buffer?.Clear();
    }

    // A see-through sphere, hidden behind walls and ground like anything else in the world, seen from inside too.
    // A local object only this game sees, removed after a while. effectName: unique per mod; with showEffect, a status
    // effect shows label and icon with the time left, and tooltip when pointed at in the inventory.
    public static void ShowSphere(Vector3 center, float radius, Color color, float seconds, string effectName, string label, Sprite? icon,
        bool showEffect = true, string? tooltip = null)
    {
        RemoveSphere();
        if (!EnsureShader()) return;
        if (!_sphereMaterial)
        {
            _sphereMaterial = new Material(_shader) { name = "ChestKit_SphereMaterial", hideFlags = HideFlags.HideAndDontSave };
            _sphereMaterial.SetFloat("unity_GUIZTestMode", (float)CompareFunction.LessEqual);
        }
        _sphereMaterial!.color = color;

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "ChestKit_Sphere";
        Object.Destroy(sphere.GetComponent<Collider>());
        sphere.transform.position = center;
        sphere.transform.localScale = Vector3.one * (radius * 2f);
        Renderer renderer = sphere.GetComponent<Renderer>();
        renderer.sharedMaterial = _sphereMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Object.Destroy(sphere, seconds);
        _sphere = sphere;
        if (showEffect) AddEffect(effectName, label, icon, seconds, tooltip);
    }

    public static void RemoveSphere()
    {
        if (_sphere) Object.Destroy(_sphere);
        _sphere = null;
        if (_sphereEffect && Player.m_localPlayer) Player.m_localPlayer.GetSEMan().RemoveStatusEffect(_sphereEffect!.NameHash(), quiet: true);
    }

    // On the local player only.
    private static void AddEffect(string name, string label, Sprite? icon, float seconds, string? tooltip)
    {
        Player player = Player.m_localPlayer;
        if (!player) return;
        if (!_sphereEffect || _sphereEffect!.name != name)
        {
            if (_sphereEffect) Object.Destroy(_sphereEffect);
            _sphereEffect = ScriptableObject.CreateInstance<StatusEffect>();
            _sphereEffect!.name = name;
            _sphereEffect.hideFlags = HideFlags.HideAndDontSave;
        }
        _sphereEffect!.m_name = label;
        _sphereEffect.m_icon = icon;
        _sphereEffect.m_tooltip = tooltip ?? string.Empty;
        _sphereEffect.m_ttl = seconds;
        SEMan effects = player.GetSEMan();
        effects.RemoveStatusEffect(_sphereEffect.NameHash(), quiet: true);
        effects.AddStatusEffect(_sphereEffect, resetTime: true);
    }

    // For when the mod unloads.
    public static void Dispose()
    {
        Stop();
        RemoveSphere();
        if (_sphereMaterial) Object.Destroy(_sphereMaterial);
        _sphereMaterial = null;
        if (_sphereEffect) Object.Destroy(_sphereEffect);
        _sphereEffect = null;
        if (_hooked) Camera.onPreRender -= OnPreRender;
        _hooked = false;
        if (_camera && _buffer != null) _camera!.RemoveCommandBuffer(RenderEvent, _buffer);
        _camera = null;
        _buffer?.Release();
        _buffer = null;
        foreach (Material material in Materials.Values)
            if (material) Object.Destroy(material);
        Materials.Clear();
    }

    private static void OnPreRender(Camera camera)
    {
        if (PatchGuard.IsDisabled(Feature)) return;
        try { Draw(camera); }
        catch (Exception exception)
        {
            _buffer?.Clear();
            PatchGuard.Fail(Feature, exception);
        }
    }

    private static void Draw(Camera camera)
    {
        GameCamera gameCamera = GameCamera.instance;
        if (!gameCamera || camera.gameObject != gameCamera.gameObject) return;
        if (camera != _camera)
        {
            if (_camera && _buffer != null) _camera!.RemoveCommandBuffer(RenderEvent, _buffer);
            _buffer ??= new CommandBuffer { name = "ChestKit Highlight" };
            camera.AddCommandBuffer(RenderEvent, _buffer);
            _camera = camera;
        }
        _buffer!.Clear();
        if (Time.unscaledTime >= _until)
        {
            if (Targets.Count > 0) Stop();
            return;
        }
        if (!EnsureShader()) return;
        if (Time.unscaledTime >= _nextRefresh) CollectRenderers();

        float alpha = Mathf.Lerp(MinAlpha, MaxAlpha, (Mathf.Sin(Time.time * PulseSpeed) + 1f) / 2f);
        foreach ((Renderer renderer, Func<Color> color) in Renderers)
        {
            // Only the level of detail the game shows right now, so overlapping copies don't stack up.
            if (!renderer || !renderer.enabled || !renderer.isVisible || !renderer.gameObject.activeInHierarchy) continue;
            int subMeshes = renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh ? skinned.sharedMesh.subMeshCount
                : renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh ? filter.sharedMesh.subMeshCount : 0;
            Material material = MaterialFor(color, alpha);
            for (int index = 0; index < subMeshes; index++) _buffer.DrawRenderer(renderer, material, index);
        }
    }

    private static Material MaterialFor(Func<Color> color, float alpha)
    {
        if (!Materials.TryGetValue(color, out Material material) || !material)
        {
            material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_StencilComp", (float)CompareFunction.Always);
            material.SetFloat("_ColorMask", (float)ColorWriteMask.All);
            material.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            Materials[color] = material;
        }
        Color value = color();
        value.a = alpha;
        material.SetColor(ColorId, value);
        return material;
    }

    // The objects' own meshes, not their particles or flames.
    private static void CollectRenderers()
    {
        _nextRefresh = Time.unscaledTime + RendererRefreshInterval;
        Renderers.Clear();
        foreach ((GameObject target, Func<Color> color) in Targets)
        {
            if (!target) continue;
            target.GetComponentsInChildren(true, Found);
            foreach (Renderer renderer in Found)
                if (renderer is MeshRenderer or SkinnedMeshRenderer) Renderers.Add((renderer, color));
            Found.Clear();
        }
    }

    // UI/Default ships with every Unity build, is unlit, blends by alpha and lets the depth test be changed.
    private static bool EnsureShader()
    {
        if (_shader) return true;
        if (_shaderMissing) return false;
        _shader = Shader.Find("UI/Default");
        if (_shader) return true;
        _shaderMissing = true;
        ChestKitCore.Logger?.LogWarning("UI/Default shader not found, highlights are disabled.");
        return false;
    }
}
