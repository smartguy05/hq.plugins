namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static class ListFolder
{
    public static DomNode Fold(DomNode root, int similarityThreshold = 60, int minSiblings = 4)
    {
        if (root == null || root.IsText) return root;

        // Recursively fold children first
        for (var i = 0; i < root.Children.Count; i++)
            root.Children[i] = Fold(root.Children[i], similarityThreshold, minSiblings);

        // Check if this node's children are repetitive
        if (root.Children.Count >= minSiblings)
        {
            var elementChildren = root.Children.Where(c => !c.IsText && c.Tag != null).ToList();
            if (elementChildren.Count >= minSiblings)
            {
                var hashes = elementChildren.Select(c => ComputeSimHash(c)).ToList();
                var groups = GroupBySimilarity(hashes, similarityThreshold);

                foreach (var group in groups.Where(g => g.Count >= minSiblings))
                {
                    // Keep first, replace rest with a summary node
                    var representative = elementChildren[group[0]];
                    var foldedCount = group.Count - 1;

                    // Remove the duplicates (in reverse order to preserve indices)
                    foreach (var idx in group.Skip(1).OrderByDescending(i => i))
                    {
                        var childIdx = root.Children.IndexOf(elementChildren[idx]);
                        if (childIdx >= 0)
                            root.Children.RemoveAt(childIdx);
                    }

                    // Insert summary after the representative
                    var repIdx = root.Children.IndexOf(representative);
                    if (repIdx >= 0)
                    {
                        root.Children.Insert(repIdx + 1, new DomNode
                        {
                            IsText = true,
                            TextContent = $"[... {foldedCount} more similar {representative.Tag} items]"
                        });
                    }
                }
            }
        }

        return root;
    }

    private static ulong ComputeSimHash(DomNode node)
    {
        // Hash the structural signature: tag names and attribute keys, ignoring values
        var features = new List<string>();
        CollectStructuralFeatures(node, features, 0, maxDepth: 5);

        ulong hash = 0;
        var weights = new int[64];

        foreach (var feature in features)
        {
            var featureHash = FnvHash(feature);
            for (var i = 0; i < 64; i++)
            {
                if ((featureHash & (1UL << i)) != 0)
                    weights[i]++;
                else
                    weights[i]--;
            }
        }

        for (var i = 0; i < 64; i++)
        {
            if (weights[i] > 0)
                hash |= 1UL << i;
        }

        return hash;
    }

    private static void CollectStructuralFeatures(DomNode node, List<string> features, int depth, int maxDepth)
    {
        if (node == null || depth > maxDepth) return;

        if (node.IsText)
        {
            features.Add($"text@{depth}");
            return;
        }

        features.Add($"{node.Tag}@{depth}");
        if (node.Role != null) features.Add($"role:{node.Role}@{depth}");
        if (node.Href != null) features.Add($"href@{depth}");
        if (node.InputType != null) features.Add($"input:{node.InputType}@{depth}");

        foreach (var child in node.Children)
            CollectStructuralFeatures(child, features, depth + 1, maxDepth);
    }

    private static List<List<int>> GroupBySimilarity(List<ulong> hashes, int thresholdPercent)
    {
        var visited = new HashSet<int>();
        var groups = new List<List<int>>();

        for (var i = 0; i < hashes.Count; i++)
        {
            if (visited.Contains(i)) continue;

            var group = new List<int> { i };
            visited.Add(i);

            for (var j = i + 1; j < hashes.Count; j++)
            {
                if (visited.Contains(j)) continue;

                var similarity = ComputeSimilarity(hashes[i], hashes[j]);
                if (similarity >= thresholdPercent)
                {
                    group.Add(j);
                    visited.Add(j);
                }
            }

            if (group.Count > 1)
                groups.Add(group);
        }

        return groups;
    }

    private static int ComputeSimilarity(ulong a, ulong b)
    {
        var xor = a ^ b;
        var differingBits = CountBits(xor);
        return (int)((64 - differingBits) * 100.0 / 64);
    }

    private static int CountBits(ulong value)
    {
        var count = 0;
        while (value != 0)
        {
            count++;
            value &= value - 1;
        }
        return count;
    }

    private static ulong FnvHash(string s)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;

        var hash = fnvOffset;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= fnvPrime;
        }
        return hash;
    }
}
