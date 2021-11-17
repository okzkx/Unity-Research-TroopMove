using UnityEngine;

public enum BlockType
{
    Moveable,
    Barrier,
    Start,
    End,
    Path
}

[ExecuteAlways]
public class Block : MonoBehaviour
{
    public MeshRenderer meshRenderer;
    public BlockType blockType;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
    }

    private void OnValidate()
    {
        SwitchType(blockType);
    }

    public void SwitchType(BlockType blockType)
    {
        this.blockType = blockType;
        
#if UNITY_EDITOR
        meshRenderer.material =
            UnityEditor.AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{blockType.ToString()}_Mat.mat");
#endif
    }
}