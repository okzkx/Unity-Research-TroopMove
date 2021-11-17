using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class GenerateBlocks : MonoBehaviour
{
    public GameObject blockGroup;

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (blockGroup != null)
        {
            DestroyImmediate(blockGroup);
        }
        
        blockGroup = new GameObject("BlockGroup");
        blockGroup.transform.parent = transform;
        Transform blockGroupTransform = blockGroup.transform;

        for (int i = 0; i < 10; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                GameObject blockGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Block block = blockGO.AddComponent<Block>();
                block.name = "Block";
                block.transform.localScale = new Vector3(0.8f, 0.01f, 0.8f);
                block.transform.parent = blockGroupTransform;
                block.transform.position = new Vector3(j + 0.5f, -0.05f, i + 0.5f);
                block.SwitchType(BlockType.Moveable);

                // GameObject block = Instantiate(blockPrefab, blockGroupTransform);
            }
        }
    }
}