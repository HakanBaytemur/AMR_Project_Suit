using ACadSharp.Entities;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;

namespace DwgTrueView.Cad;

/// <summary>
/// ODA/Teigha dynamic-block visibility: keep only the entities that belong to
/// the active <see cref="BlockVisibilityParameter"/> state saved in the file.
/// Missing or corrupt visibility data is ignored so the whole block still draws.
/// </summary>
internal static class DynamicBlockVisibility
{
    public static bool Allows(Entity? entity, BlockRecord? record)
    {
        if (entity is null)
        {
            return false;
        }
        try
        {
            if (!TryGetSets(record, out HashSet<ulong> controlled, out HashSet<ulong> visible))
            {
                return true;
            }
            ulong handle = entity.Handle;
            if (handle == 0)
            {
                return true;
            }
            return !controlled.Contains(handle) || visible.Contains(handle);
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static bool TryGetSets(
        BlockRecord? record,
        out HashSet<ulong> controlled,
        out HashSet<ulong> visible)
    {
        controlled = [];
        visible = [];
        BlockVisibilityParameter? parameter = record?.EvaluationGraph?.Nodes?
            .Select(static node => node?.Expression)
            .OfType<BlockVisibilityParameter>()
            .FirstOrDefault();
        if (parameter?.States is null || parameter.States.Count == 0)
        {
            return false;
        }

        if (parameter.Entities is not null)
        {
            foreach (Entity? owned in parameter.Entities)
            {
                if (owned is not null && owned.Handle != 0)
                {
                    controlled.Add(owned.Handle);
                }
            }
        }
        if (controlled.Count == 0)
        {
            return false;
        }

        BlockVisibilityParameter.State? state = ResolveActiveState(parameter);
        if (state?.Entities is null)
        {
            return false;
        }
        foreach (Entity? owned in state.Entities)
        {
            if (owned is not null && owned.Handle != 0)
            {
                visible.Add(owned.Handle);
            }
        }
        return true;
    }

    private static BlockVisibilityParameter.State? ResolveActiveState(
        BlockVisibilityParameter parameter)
    {
        BlockVisibilityParameter.State[] states = parameter.States?.Values
            .Where(static state => state is not null)
            .ToArray() ?? [];
        if (states.Length == 0)
        {
            return null;
        }
        string? evaluated = null;
        try
        {
            evaluated = FormatEvaluated(parameter.EvaluatedValue);
        }
        catch (Exception)
        {
            evaluated = null;
        }
        if (!string.IsNullOrWhiteSpace(evaluated) && parameter.States is not null)
        {
            if (parameter.States.TryGetValue(evaluated, out BlockVisibilityParameter.State? named)
                && named is not null)
            {
                return named;
            }
            if (int.TryParse(evaluated, out int index)
                && (uint)index < (uint)states.Length)
            {
                return states[index];
            }
        }
        return states[0];
    }

    private static string? FormatEvaluated(DxfValuePair? pair)
    {
        if (pair is null)
        {
            return null;
        }
        string? text = pair.ToString();
        if (string.IsNullOrWhiteSpace(text) || text == pair.GetType().Name)
        {
            return null;
        }
        return text.Trim();
    }
}
