using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class TroopMove : MonoBehaviour
{
    public bool useAnimation = true;
    public bool useCatmullRomCurve = true;

    public int curveSegmentSeparateCount = 100;
    public float unitPerSecond = 1;
    public float runSpeedScale = 1.5f;
    public int soldierRowCount = 4;
    public int soldierColCount = 4;
    public float phalanxWidth = 1;
    public float phalanxHeight = 1;

    public Soldier soldierPrefab;
    private int soldierCount;

    private const int MAP_SIDE_COUNT = 10;

    private LineRenderer lineRenderer;

    // Map
    Block[][] map;
    private Vector2Int start;
    private Vector2Int end;

    // Path
    private List<Vector2Int> paths = new List<Vector2Int>();

    // Curve
    private List<Vector3> curvePoints = new List<Vector3>();
    private float curveLength;

    // Update
    private Soldier[] soldiers;
    private Vector3[] soldierTargetPositions;
    private Quaternion[] soldierTargetRotations;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        soldierCount = soldierRowCount * soldierColCount;
        soldiers = new Soldier[soldierCount];
        // Spawn soldiers
        for (int i = 0; i < soldierCount; i++)
        {
            soldiers[i] = Instantiate(soldierPrefab, transform);
        }

        soldierTargetPositions = new Vector3[soldierCount];
        soldierTargetRotations = new Quaternion[soldierCount];
    }

    void Start()
    {
        InitMap();
        // Path for grid's id
        GeneratePath();
        // Troop traveling points in world space
        GenerateCurvePoints();
    }

    private void Update()
    {
        // Simulation ： Update soldier target position and rotation
        float unitPerFrame = unitPerSecond * Time.deltaTime;
        float distanceTravelled = unitPerSecond * Time.time;
        float moveProgress = distanceTravelled / curveLength;
        if (moveProgress <= 1)
        {
            float c = (curvePoints.Count - 1) * moveProgress;
            var segmentStart = curvePoints[Mathf.FloorToInt(c)];
            var segmentEnd = curvePoints[Mathf.CeilToInt(c)];
            Vector3 forwardWS = Vector3.Normalize(segmentEnd - segmentStart);
            var rightAngleMatrix = float4x4.Euler(0, math.PI * 0.5f, 0);
            Vector3 rightWS = math.rotate(rightAngleMatrix, forwardWS);

            for (int i = 0; i < soldierRowCount; i++)
            {
                for (int j = 0; j < soldierColCount; j++)
                {
                    // (j, i) in rect(0,0, soldierRowCount - 1, soldierColCount - 1)
                    // (x, y) in rect(-0.5 * width, -height, width, height)
                    var soldierPositionPhalanxOS = math.remap(
                        new float2(0, 0),
                        new float2(soldierColCount - 1, soldierRowCount - 1),
                        new float2(-0.5f * phalanxWidth, -phalanxHeight),
                        new float2(0.5f * phalanxWidth, 0),
                        new float2(j, i));

                    Vector3 center = Vector3.Lerp(segmentStart, segmentEnd, math.frac(c));
                    soldierTargetPositions[i * soldierColCount + j] =
                        center + rightWS * soldierPositionPhalanxOS.x + forwardWS * soldierPositionPhalanxOS.y;

                    float localToWorldRotation = math.acos(math.dot(Vector3.forward, forwardWS));
                    localToWorldRotation = forwardWS.x < 0 ? 2 * math.PI - localToWorldRotation : localToWorldRotation;
                    soldierTargetRotations[i * soldierColCount + j] =
                        quaternion.AxisAngle(math.up(), localToWorldRotation);
                }
            }

            // Debug : Brightness curve in used
            float c2 = paths.Count * moveProgress;
            float colorGradiantStart = Mathf.Floor(c2) / paths.Count;
            float colorGradiantEnd = Mathf.Ceil(c2) / paths.Count;
            Gradient gradient = lineRenderer.colorGradient;
            var keys = gradient.colorKeys;
            keys[1].time = colorGradiantStart;
            keys[2].time = colorGradiantEnd;
            gradient.SetKeys(keys, gradient.alphaKeys);
            lineRenderer.colorGradient = gradient;
        }

        // Animation : slowly translate soldier transform to target in meterPerFrame
        float meterPerFrameToTarget = unitPerFrame * runSpeedScale; // run to target
        for (int i = 0; i < soldierCount; i++)
        {
            // Position
            Vector3 targetPosition = soldierTargetPositions[i];
            Transform soldier = soldiers[i].transform;
            Vector3 v = targetPosition - soldier.position;

            if (v.Equals(Vector3.zero) ||
                Vector3.Magnitude(v) < meterPerFrameToTarget ||
                soldier.transform.position.Equals(Vector3.zero) ||
                !useAnimation)
            {
                soldier.transform.position = targetPosition;
            }
            else
            {
                soldier.position = Vector3.Normalize(v) * meterPerFrameToTarget + soldier.position;
            }

            // Rotation
            soldier.rotation = soldierTargetRotations[i];

            // more complex rotate, possibly make shaking
            // if (!v.Equals(Vector3.zero))
            // {
            //     Vector3 forwardWS = Vector3.Normalize(v);
            //     float localToWorldRotation = math.acos(math.dot(Vector3.forward, forwardWS));
            //     localToWorldRotation = forwardWS.x < 0 ? 2 * math.PI - localToWorldRotation : localToWorldRotation;
            //     soldier.rotation = quaternion.AxisAngle(math.up(), localToWorldRotation);
            // }
        }
    }

    private void InitMap()
    {
        map = new Block[MAP_SIDE_COUNT][];
        for (int i = 0; i < MAP_SIDE_COUNT; i++)
        {
            map[i] = new Block[MAP_SIDE_COUNT];
        }

        GenerateBlocks generateBlocks = FindObjectOfType<GenerateBlocks>();
        for (int i = 0; i < MAP_SIDE_COUNT * MAP_SIDE_COUNT; i++)
        {
            int x = i / MAP_SIDE_COUNT;
            int y = i % MAP_SIDE_COUNT;

            var go = generateBlocks.blockGroup.transform.GetChild(i);
            Block block = go.GetComponent<Block>();
            map[x][y] = block;

            if (block.blockType == BlockType.Start)
            {
                start = new Vector2Int(x, y);
            }

            if (block.blockType == BlockType.End)
            {
                end = new Vector2Int(x, y);
            }
        }
    }

    #region Path AStar

    class Grid : IComparable<Grid>
    {
        public Vector2Int pos;
        public float f;
        public float g;
        public float h => f + g;
        public Grid pre;

        public int CompareTo(Grid other)
        {
            return (int) ((this.h - other.h) * 10000);
        }
    }

    /// <summary>
    /// AStar
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    private void GeneratePath()
    {
        Grid[][] grids = new Grid[MAP_SIDE_COUNT][];
        for (int i = 0; i < MAP_SIDE_COUNT; i++)
        {
            grids[i] = new Grid[MAP_SIDE_COUNT];
            for (int j = 0; j < MAP_SIDE_COUNT; j++)
            {
                grids[i][j] = new Grid()
                {
                    pos = new Vector2Int(i, j),
                    g = Vector2Int.Distance(new Vector2Int(i, j), end),
                };
            }
        }

        List<Grid> open = new List<Grid>();
        List<Grid> close = new List<Grid>();

        open.Add(grids[start.x][start.y]);

        while (open.Count != 0)
        {
            Grid toAppend = open.Min();

            if (toAppend.pos == end)
            {
                Grid p = toAppend;
                do
                {
                    paths.Insert(0, p.pos);
                    p = p.pre;
                } while (p != null);

                // Debug grid color in editor
                foreach (var pos in paths)
                {
                    if (pos != start && pos != end)
                    {
                        map[pos.x][pos.y].SwitchType(BlockType.Path);
                    }
                }

                return;
            }

            open.Remove(toAppend);
            close.Add(toAppend);

            UpdateNeighbor(grids, toAppend, new Vector2Int(-1, 0), open, close);
            UpdateNeighbor(grids, toAppend, new Vector2Int(1, 0), open, close);
            UpdateNeighbor(grids, toAppend, new Vector2Int(0, 1), open, close);
            UpdateNeighbor(grids, toAppend, new Vector2Int(0, -1), open, close);
        }
    }

    private void UpdateNeighbor(Grid[][] grids, Grid toAppend, Vector2Int offset, List<Grid> open, List<Grid> close)
    {
        Vector2Int pos = toAppend.pos + offset;
        if (pos.x > 0 && pos.x < MAP_SIDE_COUNT && pos.y > 0 && pos.y < MAP_SIDE_COUNT)
        {
            if (map[pos.x][pos.y].blockType != BlockType.Barrier)
            {
                Grid g = grids[pos.x][pos.y];
                bool inOpen = open.Contains(g);
                if (!close.Contains(g))
                {
                    float newF = toAppend.f + 1;
                    if (g.f > newF || !inOpen)
                    {
                        g.f = newF;
                        g.pre = toAppend;
                    }

                    if (!inOpen)
                    {
                        open.Add(g);
                    }
                }
            }
        }
    }

    #endregion

    #region CatmulRom Curve

    private void GenerateCurvePoints()
    {
        for (int i = 0; i < paths.Count - 1; i++)
        {
            AppendPathCurve(curvePoints, i);
        }

        for (int i = 0; i < curvePoints.Count - 1; i++)
        {
            curveLength += Vector3.Magnitude(curvePoints[i + 1] - curvePoints[i]);
        }

        // Debug in line renderer
        lineRenderer.positionCount = curvePoints.Count;
        lineRenderer.SetPositions(curvePoints.ToArray());
    }

    private void AppendPathCurve(List<Vector3> curvePoints, int i)
    {
        if (curvePoints.Count > 0)
        {
            curvePoints.RemoveAt(curvePoints.Count - 1);
        }

        var b = ToPositionWS(paths[i]);
        var c = ToPositionWS(paths[i + 1]);
        var a = i == 0 ? 2 * b - c : ToPositionWS(paths[i - 1]);
        var d = i + 2 >= paths.Count ? 2 * c - b : ToPositionWS(paths[i + 2]);

        if (useCatmullRomCurve)
        {
            AppendCatmullRomPoints(curveSegmentSeparateCount, curvePoints, a, b, c, d);
        }
        else
        {
            curvePoints.Add(b);
            curvePoints.Add(c);
        }
    }

    private Vector3 ToPositionWS(Vector2Int p)
    {
        return new Vector3(p.y + 0.5f, 0, p.x + 0.5f);
    }

    /// <summary>
    /// 由四个控制点采样出 CatmullRom 曲线点集合
    /// </summary>
    /// <param name="segmentCount"></param>
    /// <param name="pos"></param>
    /// <param name="p0"></param>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <param name="p3"></param>
    /// <exception cref="https://blog.csdn.net/u012154588/article/details/98977717"></exception>
    private void AppendCatmullRomPoints(int segmentCount, List<Vector3> pos, Vector3 p0, Vector3 p1, Vector3 p2,
        Vector3 p3)
    {
        float resolution = 1f / segmentCount;
        for (int i = 0; i <= segmentCount; i++)
        {
            pos.Add(SampleCatmullPoint(i * resolution, p0, p1, p2, p3));
        }
    }

    private static Vector3 SampleCatmullPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Vector3 a = 2 * p1;
        Vector3 b = p2 - p0;
        Vector3 c = 2 * p0 - 5 * p1 + 4 * p2 - p3;
        Vector3 d = -p0 + 3 * p1 - 3 * p2 + p3;

        Vector3 pos = 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));

        return pos;
    }

    #endregion
}