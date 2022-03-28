using System;
using System.Collections.Generic;
using System.Linq;
using QuickGraph;
using QuickGraph.Algorithms;

namespace UrDeliveries.Models.SpiderLegModels
{
    public interface ISpiderLegBuilder
    {
        List<RouteAdjacency> Build(List<Route> routes,
            Site reference, List<double> distancesToReference, TransitMatrix transitMatrix);
    }

    public class SpiderLegBuilder : ISpiderLegBuilder
    {
        public List<RouteAdjacency> Build(List<Route> routes,
            Site reference, List<double> distancesToReference, TransitMatrix transitMatrix)
        {
            var edgeCostDictionary = CreateEdges(routes, reference, transitMatrix, distancesToReference);
            return CreateAdjacenciesFromEdges(routes, reference, edgeCostDictionary);
        }

        private double? GetClusterToClusterDistance(TransitMatrix transitMatrix, List<Site> cluster1, List<Site> cluster2)
        {
            return GetClusterToClusterDistanceClosestSites(transitMatrix, cluster1, cluster2);
        }

        private double? GetClusterToClusterDistanceClosestSites(TransitMatrix transitMatrix, List<Site> cluster1, List<Site> cluster2)
        {
            if (cluster1.Count == 0 || cluster2.Count == 0)
            {
                return null;
            }

            var siteToSiteDistances = new List<double>();
            foreach (var site1 in cluster1)
            {
                foreach (var site2 in cluster2)
                {
                    var d = transitMatrix.GetUndirectedDistanceBetween(site1.Id, site2.Id);
                    siteToSiteDistances.Add(d);
                }
            }

            return siteToSiteDistances
                .OrderBy(d => d)
                .Take((int)Math.Ceiling(siteToSiteDistances.Count / 2.0))
                .Average();
        }

        private double? GetClusterToClusterDistanceAllSites(TransitMatrix transitMatrix, List<Site> cluster1, List<Site> cluster2)
        {
            if (cluster1.Count == 0 || cluster2.Count == 0)
            {
                return null;
            }

            var averageClusterToClusterDistance = 0.0;
            foreach (var site1 in cluster1)
            {
                foreach (var site2 in cluster2)
                {
                    var d = transitMatrix.GetUndirectedDistanceBetween(site1.Id, site2.Id);
                    averageClusterToClusterDistance += d;
                }
            }

            return averageClusterToClusterDistance / (cluster1.Count * cluster2.Count);
        }

        private Dictionary<Edge<int>, double> CreateEdges(List<Route> routes, Site reference, TransitMatrix transitMatrix, List<double> distancesToReference)
        {
            var clusters = GetRoutesAsClusters(routes, reference);
            var edgeCostDictionary = new Dictionary<Edge<int>, double>();
            for (var i = 0; i < clusters.Count; i++)
            {
                for (var j = i + 1; j < clusters.Count; j++)
                {
                    var clusterToClusterDistance = GetClusterToClusterDistance(transitMatrix, clusters[i], clusters[j]) ?? 1e6;

                    if (distancesToReference != null && clusters[j].Count > 0 && clusters[j][0] == reference)
                    {
                        clusterToClusterDistance = distancesToReference[i];
                    }
                    var edge = new Edge<int>(i, j);
                    var reverseEdge = new Edge<int>(j, i);
                    var d3 = clusterToClusterDistance * clusterToClusterDistance * clusterToClusterDistance;
                    var d1 = Math.Max(clusterToClusterDistance - 2, 0);
                    edgeCostDictionary.Add(edge, d3);
                    edgeCostDictionary.Add(reverseEdge, d3);
                }
            }
            return edgeCostDictionary;
        }

        private List<RouteAdjacency> CreateAdjacenciesFromEdges(List<Route> routes, Site reference, Dictionary<Edge<int>, double> edgeCostDictionary)
        {
            // Compute shortest path from each node
            var clusters = GetRoutesAsClusters(routes, reference);
            var referenceIdx = clusters.Count - 1;
            var edges = edgeCostDictionary.Keys;
            var graph = edges.ToBidirectionalGraph<int, Edge<int>>();
            var edgeCost = AlgorithmExtensions.GetIndexer(edgeCostDictionary);
            var tryGetPaths = graph.ShortestPathsDijkstra(edgeCost, referenceIdx);
            var paths = new List<IEnumerable<Edge<int>>>();
            for (var i = 0; i < clusters.Count - 1; i++)
            {
                if (tryGetPaths(i, out var path))
                {
                    paths.Add(path);
                }
            }

            // Add the edges in sequence
            paths = paths.OrderByDescending(p => p.Count()).ToList();
            var sequence = 0;
            var selectedEdges = new List<Edge<int>>();
            var sequences = new List<int>();
            paths.ForEach(path =>
            {
                path.Reverse().ToList().ForEach(edge =>
                {
                    if (edge.Source == referenceIdx || edge.Target == referenceIdx) return;

                    var idx = selectedEdges.IndexOf(edge);
                    if (idx == -1)
                    {
                        selectedEdges.Add(edge);
                        sequences.Add(sequence);
                    }
                    else
                    {
                        sequences[idx] = sequence;
                    }
                    sequence++;
                });
            });

            var adjacencies = new List<RouteAdjacency>();
            var edgeIdx = 0;
            selectedEdges.ForEach(edge =>
            {
                var adjacency = new RouteAdjacency
                {
                    InnerRoute = routes.ElementAt(edge.Source),
                    OuterRoute = routes.ElementAt(edge.Target),
                    Sequence = sequences.ElementAt(edgeIdx++)
                };
                adjacencies.Add(adjacency);
            });
            return adjacencies.OrderBy(a => a.Sequence).ToList();
        }

        private List<List<Site>> GetRoutesAsClusters(List<Route> routes, Site reference)
        {
            // Add reference point as a cluster
            var clusters = routes.Select(r => r.Sites).ToList();
            clusters.Add(new List<Site> { reference });
            return clusters;
        }
    }
}