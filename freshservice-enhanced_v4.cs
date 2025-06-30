using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            // Handle potential base64 encoding of OperationId
            string realOperationId = this.Context.OperationId;
            try 
            {
                byte[] data = Convert.FromBase64String(this.Context.OperationId);
                realOperationId = System.Text.Encoding.UTF8.GetString(data);
            }
            catch (FormatException) 
            {
                // Not base64 encoded, use as-is
            }
            
            // Handle webhook validations for triggers
            if (this.Context.Request.Method == HttpMethod.Post && IsWebhookValidation())
            {
                return await HandleWebhookValidation().ConfigureAwait(false);
            }
            
            // Check for natural language query and create modified request if needed
            var requestToSend = await GetRequestWithTransformedQuery(realOperationId).ConfigureAwait(false);
            
            // For actions, send the request and transform the response
            var response = await this.Context.SendAsync(requestToSend, this.CancellationToken).ConfigureAwait(false);
            
            // Route to appropriate handler based on operation ID
            switch (realOperationId?.ToLower())
            {
                // Ticket Actions
                case "listtickets":
                    return await TransformListTicketsResponse(response).ConfigureAwait(false);
                case "getticket":
                    return await TransformGetTicketResponse(response).ConfigureAwait(false);
                case "getticketconversations":
                    return await TransformTicketConversationsResponse(response).ConfigureAwait(false);
                case "gettickettimeentries":
                    return await TransformTicketTimeEntriesResponse(response).ConfigureAwait(false);
                    
                // Problem Actions
                case "listproblems":
                    return await TransformListProblemsResponse(response).ConfigureAwait(false);
                case "getproblem":
                    return await TransformGetProblemResponse(response).ConfigureAwait(false);
                    
                // Change Actions
                case "listchanges":
                    return await TransformListChangesResponse(response).ConfigureAwait(false);
                case "getchange":
                    return await TransformGetChangeResponse(response).ConfigureAwait(false);
                    
                // Release Actions
                case "listreleases":
                    return await TransformListReleasesResponse(response).ConfigureAwait(false);
                case "getrelease":
                    return await TransformGetReleaseResponse(response).ConfigureAwait(false);
                    
                // Agent Actions
                case "listagents":
                    return await TransformListAgentsResponse(response).ConfigureAwait(false);
                case "getagent":
                    return await TransformGetAgentResponse(response).ConfigureAwait(false);
                    
                // Requester Actions
                case "listrequesters":
                    return await TransformListRequestersResponse(response).ConfigureAwait(false);
                case "getrequester":
                    return await TransformGetRequesterResponse(response).ConfigureAwait(false);
                    
                // Workspace Actions
                case "listworkspaces":
                    return await TransformListWorkspacesResponse(response).ConfigureAwait(false);
                case "getworkspace":
                    return await TransformGetWorkspaceResponse(response).ConfigureAwait(false);
                    
                // Location Actions
                case "listlocations":
                    return await TransformListLocationsResponse(response).ConfigureAwait(false);
                case "getlocation":
                    return await TransformGetLocationResponse(response).ConfigureAwait(false);
                    
                // Webhook Triggers
                case "newticket":
                case "ticketupdated":
                case "ticketclosed":
                case "problemcreated":
                case "changeapproved":
                case "highpriorityticket":
                case "slaviolation":
                case "agentassigned":
                    return await HandleWebhookTrigger(realOperationId).ConfigureAwait(false);
                    
                default:
                    // Return the response as-is if no transformation needed
                    return response;
            }
        }
        catch (OverflowException ex)
        {
            // Log the error internally but don't expose details
            this.Context.Logger.LogError($"Integer overflow error: {ex.Message}");
            var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            errorResponse.Content = CreateJsonContent(new JObject
            {
                ["error"] = new JObject
                {
                    ["message"] = "An error occurred processing numeric values. Please contact support.",
                    ["type"] = "DataProcessingError",
                    ["correlationId"] = this.Context.CorrelationId
                }
            }.ToString());
            return errorResponse;
        }
        catch (Exception ex)
        {
            // Log the error internally but don't expose stack trace
            this.Context.Logger.LogError($"Script execution error: {ex.Message}\n{ex.StackTrace}");
            var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            errorResponse.Content = CreateJsonContent(new JObject
            {
                ["error"] = new JObject
                {
                    ["message"] = "An unexpected error occurred. Please try again later.",
                    ["type"] = ex.GetType().Name,
                    ["correlationId"] = this.Context.CorrelationId
                }
            }.ToString());
            return errorResponse;
        }
    }
    
    #region Response Transformations
    
    private async Task<HttpResponseMessage> TransformListTicketsResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        
        var tickets = data["tickets"] as JArray ?? new JArray();
        
        // Enhanced ticket processing
        foreach (var ticket in tickets)
        {
            EnrichTicketData(ticket);
            AddTicketSemanticContext(ticket);
        }
        
        // Create enhanced response with V4 deeper analytics
        var enhancedResponse = new JObject
        {
            ["tickets"] = tickets,
            ["metadata"] = new JObject
            {
                ["count"] = tickets.Count,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["has_more"] = data["link_to_next_page"] != null
            },
            ["summary"] = GenerateTicketListSummary(tickets),
            ["insights"] = GenerateEnhancedTicketInsights(tickets), // V4: Enhanced insights
            ["analytics"] = GenerateAdvancedAnalytics(tickets)       // V4: New advanced analytics
        };
        
        // Add natural language query interpretation if applicable
        var queryParams = HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
        var naturalQuery = queryParams["natural_query"] ?? queryParams["nlq"];
        if (!string.IsNullOrEmpty(naturalQuery))
        {
            enhancedResponse["query_interpretation"] = new JObject
            {
                ["original_query"] = HttpUtility.UrlDecode(naturalQuery),
                ["interpreted_filters"] = queryParams["query"] ?? string.Empty,
                ["interpretation_confidence"] = "High"
            };
        }
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(enhancedResponse.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformGetTicketResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var ticket = data["ticket"] as JObject;
        
        if (ticket != null)
        {
            EnrichTicketData(ticket);
            AddTicketSemanticContext(ticket);
            AddTicketWorkflowHints(ticket);
            
            // Add summary if stats available
            if (ticket["stats"] != null)
            {
                ticket["summary"] = new JObject
                {
                    ["total_replies"] = ticket["stats"]["agent_responded_at"] != null ? 1 : 0,
                    ["has_attachments"] = ticket["attachments"]?.Any() ?? false,
                    ["is_escalated"] = ticket["is_escalated"] ?? false
                };
            }
        }
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformTicketConversationsResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var conversations = data["conversations"] as JArray ?? new JArray();
        
        var grouped = new JObject
        {
            ["conversations"] = conversations,
            ["summary"] = new JObject
            {
                ["total_count"] = conversations.Count,
                ["public_notes"] = conversations.Count(c => c["private"]?.Value<bool>() == false),
                ["private_notes"] = conversations.Count(c => c["private"]?.Value<bool>() == true),
                ["latest_update"] = conversations.FirstOrDefault()?["created_at"]
            }
        };
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(grouped.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformTicketTimeEntriesResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var timeEntries = data["time_entries"] as JArray ?? new JArray();
        
        var totalMinutes = timeEntries.Sum(te => te["time_spent"]?.Value<int>() ?? 0);
        
        var enhanced = new JObject
        {
            ["time_entries"] = timeEntries,
            ["summary"] = new JObject
            {
                ["total_entries"] = timeEntries.Count,
                ["total_minutes"] = totalMinutes,
                ["total_hours"] = Math.Round(totalMinutes / 60.0, 2),
                ["total_formatted"] = FormatTimeSpent(totalMinutes)
            }
        };
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(enhanced.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformListProblemsResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var problems = data["problems"] as JArray ?? new JArray();
        
        foreach (var problem in problems)
        {
            EnrichProblemData(problem);
        }
        
        // V4: Add enhanced insights for problems
        data["insights"] = GenerateEnhancedProblemInsights(problems);
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformGetProblemResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var problem = data["problem"] as JObject;
        
        if (problem != null)
        {
            EnrichProblemData(problem);
        }
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformListChangesResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var changes = data["changes"] as JArray ?? new JArray();
        
        foreach (var change in changes)
        {
            EnrichChangeData(change);
        }
        
        // V4: Add enhanced insights for changes
        data["insights"] = GenerateEnhancedChangeInsights(changes);
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformGetChangeResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var change = data["change"] as JObject;
        
        if (change != null)
        {
            EnrichChangeData(change);
        }
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformListReleasesResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var releases = data["releases"] as JArray ?? new JArray();
        
        foreach (var release in releases)
        {
            EnrichReleaseData(release);
        }
        
        // V4: Add enhanced insights for releases
        data["insights"] = GenerateEnhancedReleaseInsights(releases);
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformGetReleaseResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var release = data["release"] as JObject;
        
        if (release != null)
        {
            EnrichReleaseData(release);
        }
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformListAgentsResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var agents = data["agents"] as JArray ?? new JArray();
        
        data["summary"] = new JObject
        {
            ["total_agents"] = agents.Count,
            ["active_agents"] = agents.Count(a => a["active"]?.Value<bool>() == true),
            ["full_time_agents"] = agents.Count(a => a["type"]?.ToString() == "full_time"),
            ["occasional_agents"] = agents.Count(a => a["type"]?.ToString() == "occasional")
        };
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformGetAgentResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var agent = data["agent"] as JObject;
        
        if (agent != null)
        {
            agent["type_label"] = agent["type"]?.ToString() == "full_time" ? "Full Time" : "Occasional";
            agent["status_label"] = agent["active"]?.Value<bool>() == true ? "Active" : "Inactive";
        }
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformListRequestersResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var requesters = data["requesters"] as JArray ?? new JArray();
        
        data["summary"] = new JObject
        {
            ["total_requesters"] = requesters.Count,
            ["active_requesters"] = requesters.Count(r => r["active"]?.Value<bool>() == true),
            ["vip_requesters"] = requesters.Count(r => r["is_vip"]?.Value<bool>() == true)
        };
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformGetRequesterResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var requester = data["requester"] as JObject;
        
        if (requester != null)
        {
            requester["display_name"] = $"{requester["first_name"]} {requester["last_name"]}".Trim();
            requester["status_label"] = requester["active"]?.Value<bool>() == true ? "Active" : "Inactive";
        }
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformListWorkspacesResponse(HttpResponseMessage response)
    {
        return response;
    }
    
    private async Task<HttpResponseMessage> TransformGetWorkspaceResponse(HttpResponseMessage response)
    {
        return response;
    }
    
    private async Task<HttpResponseMessage> TransformListLocationsResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return response;
            
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var locations = data["locations"] as JArray ?? new JArray();
        
        var rootLocations = locations.Count(l => l["parent_location_id"] == null);
        
        data["summary"] = new JObject
        {
            ["total_locations"] = locations.Count,
            ["root_locations"] = rootLocations,
            ["child_locations"] = locations.Count - rootLocations
        };
        
        var newResponse = new HttpResponseMessage(response.StatusCode);
        newResponse.Content = CreateJsonContent(data.ToString());
        CopyHeaders(response, newResponse);
        return newResponse;
    }
    
    private async Task<HttpResponseMessage> TransformGetLocationResponse(HttpResponseMessage response)
    {
        return response;
    }
    
    #endregion
    
    #region V4 Enhanced Analytics Methods
    
    /// <summary>
    /// V4: Generate enhanced ticket insights with deeper analytics
    /// </summary>
    private JObject GenerateEnhancedTicketInsights(JArray tickets)
    {
        var insights = new JObject();
        
        // Basic metrics (enhanced from V3) - using safe integer parsing
        insights["metrics"] = new JObject
        {
            ["total_count"] = tickets.Count,
            ["average_age_hours"] = tickets.Any() ? Math.Round(tickets.Average(t => SafeGetIntOrDefault(t["age_hours"])), 1) : 0,
            ["average_age_days"] = tickets.Any() ? Math.Round(tickets.Average(t => SafeGetIntOrDefault(t["age_days"])), 1) : 0,
            ["unresolved_count"] = tickets.Count(t => {
                var status = SafeGetInt(t["status"]);
                return status.HasValue && status != 4 && status != 5;
            }),
            ["resolved_today"] = tickets.Count(t => {
                var status = SafeGetInt(t["status"]);
                return status == 4 && 
                    t["updated_at"] != null && 
                    DateTime.Parse(t["updated_at"].ToString()).Date == DateTime.UtcNow.Date;
            }),
            ["new_today"] = tickets.Count(t => 
                t["created_at"] != null && 
                DateTime.Parse(t["created_at"].ToString()).Date == DateTime.UtcNow.Date)
        };
        
        // Trend analysis
        insights["trends"] = new JObject
        {
            ["volume_trend"] = AnalyzeVolumeTrend(tickets),
            ["resolution_rate_trend"] = CalculateResolutionTrend(tickets),
            ["response_time_trend"] = AnalyzeResponseTimeTrend(tickets),
            ["common_issues"] = IdentifyCommonIssues(tickets),
            ["emerging_patterns"] = DetectEmergingPatterns(tickets)
        };
        
        // Risk analysis (enhanced) - using safe integer parsing
        insights["risks"] = new JObject
        {
            ["sla_breach_count"] = tickets.Count(t => IsApproachingSLA(t)),
            ["overdue_count"] = tickets.Count(t => IsOverdue(t)),
            ["high_age_tickets"] = tickets.Count(t => SafeGetIntOrDefault(t["age_hours"]) > 72),
            ["unassigned_urgent"] = tickets.Count(t => {
                var priority = SafeGetInt(t["priority"]);
                return priority.HasValue && priority >= 3 && t["responder_id"] == null;
            }),
            ["stale_waiting_customer"] = tickets.Count(t => {
                var status = SafeGetInt(t["status"]);
                var ageHours = SafeGetIntOrDefault(t["age_hours"]);
                return status == 6 && ageHours > 48;
            }),
            ["critical_vip_issues"] = tickets.Count(t => {
                var priority = SafeGetInt(t["priority"]);
                return priority.HasValue && priority >= 3 && t["requester"]?["is_vip"]?.Value<bool>() == true;
            })
        };
        
        // Workload distribution
        insights["workload"] = AnalyzeWorkloadDistribution(tickets);
        
        // Performance metrics
        insights["performance"] = new JObject
        {
            ["average_resolution_hours"] = CalculateAverageResolutionTime(tickets),
            ["first_response_compliance"] = CalculateFirstResponseCompliance(tickets),
            ["reopened_ticket_rate"] = CalculateReopenedRate(tickets)
        };
        
        // Natural language summaries
        insights["executive_summary"] = GenerateExecutiveSummary(insights);
        insights["actionable_recommendations"] = GenerateActionableRecommendations(insights, tickets);
        
        return insights;
    }
    
    /// <summary>
    /// V4: Generate advanced analytics for deeper insights
    /// </summary>
    private JObject GenerateAdvancedAnalytics(JArray tickets)
    {
        var analytics = new JObject();
        
        // Category distribution analysis
        analytics["category_analysis"] = AnalyzeCategoryDistribution(tickets);
        
        // Time-based patterns
        analytics["temporal_patterns"] = new JObject
        {
            ["peak_hours"] = AnalyzePeakHours(tickets),
            ["day_of_week_distribution"] = AnalyzeDayOfWeekDistribution(tickets),
            ["monthly_trends"] = AnalyzeMonthlyTrends(tickets)
        };
        
        // Agent performance analytics
        analytics["agent_analytics"] = AnalyzeAgentPerformance(tickets);
        
        // Customer satisfaction indicators
        analytics["customer_impact"] = new JObject
        {
            ["vip_satisfaction"] = AnalyzeVIPSatisfaction(tickets),
            ["response_time_by_priority"] = AnalyzeResponseTimeByPriority(tickets),
            ["escalation_patterns"] = AnalyzeEscalationPatterns(tickets)
        };
        
        // Predictive insights
        analytics["predictive_insights"] = new JObject
        {
            ["expected_resolution_load"] = PredictResolutionLoad(tickets),
            ["risk_score"] = CalculateOverallRiskScore(tickets),
            ["resource_requirements"] = EstimateResourceRequirements(tickets)
        };
        
        return analytics;
    }
    
    /// <summary>
    /// Analyze volume trend over time
    /// </summary>
    private string AnalyzeVolumeTrend(JArray tickets)
    {
        if (!tickets.Any()) return "insufficient_data";
        
        // Group tickets by creation date
        var ticketsByDate = tickets
            .Where(t => t["created_at"] != null)
            .GroupBy(t => DateTime.Parse(t["created_at"].ToString()).Date)
            .OrderBy(g => g.Key)
            .ToList();
        
        if (ticketsByDate.Count < 2) return "stable";
        
        // Calculate trend
        // Use Skip instead of TakeLast for compatibility with older .NET versions
        var skipCount = Math.Max(0, ticketsByDate.Count - 3);
        var recentAvg = ticketsByDate.Skip(skipCount).Average(g => g.Count());
        var previousAvg = ticketsByDate.Take(Math.Max(1, ticketsByDate.Count - 3)).Average(g => g.Count());
        
        var percentChange = ((recentAvg - previousAvg) / previousAvg) * 100;
        
        if (percentChange > 20) return "increasing_significantly";
        if (percentChange > 5) return "increasing";
        if (percentChange < -20) return "decreasing_significantly";
        if (percentChange < -5) return "decreasing";
        
        return "stable";
    }
    
    /// <summary>
    /// Calculate resolution rate trend
    /// </summary>
    private string CalculateResolutionTrend(JArray tickets)
    {
        var resolvedTickets = tickets.Where(t => {
            var status = SafeGetInt(t["status"]);
            return status == 4 || status == 5;
        });
        var openTickets = tickets.Where(t => {
            var status = SafeGetInt(t["status"]);
            return status == 2 || status == 3;
        });
        
        if (!tickets.Any()) return "no_data";
        
        var resolutionRate = (double)resolvedTickets.Count() / tickets.Count * 100;
        
        if (resolutionRate >= 80) return "excellent";
        if (resolutionRate >= 60) return "good";
        if (resolutionRate >= 40) return "needs_improvement";
        
        return "poor";
    }
    
    /// <summary>
    /// Analyze response time trends
    /// </summary>
    private string AnalyzeResponseTimeTrend(JArray tickets)
    {
        // This would analyze first response times if the data was available
        // For now, we'll base it on ticket age for open tickets
        var openTickets = tickets.Where(t => SafeGetInt(t["status"]) == 2);
        if (!openTickets.Any()) return "no_open_tickets";
        
        var avgAge = openTickets.Average(t => SafeGetIntOrDefault(t["age_hours"]));
        
        if (avgAge < 4) return "excellent";
        if (avgAge < 8) return "good";
        if (avgAge < 24) return "needs_improvement";
        
        return "critical";
    }
    
    /// <summary>
    /// Identify common issues from ticket patterns
    /// </summary>
    private JArray IdentifyCommonIssues(JArray tickets)
    {
        var commonIssues = new JArray();
        var issueGroups = new Dictionary<string, int>();
        
        // Analyze ticket subjects and categorizations
        foreach (var ticket in tickets)
        {
            var category = ticket["enhanced_context"]?["categorization_suggestions"]?["primary"]?.ToString() ?? "General";
            
            if (issueGroups.ContainsKey(category))
                issueGroups[category]++;
            else
                issueGroups[category] = 1;
        }
        
        // Return top 5 issues
        foreach (var issue in issueGroups.OrderByDescending(kvp => kvp.Value).Take(5))
        {
            commonIssues.Add(new JObject
            {
                ["category"] = issue.Key,
                ["count"] = issue.Value,
                ["percentage"] = Math.Round((double)issue.Value / tickets.Count * 100, 1)
            });
        }
        
        return commonIssues;
    }
    
    /// <summary>
    /// Detect emerging patterns in recent tickets
    /// </summary>
    private JArray DetectEmergingPatterns(JArray tickets)
    {
        var patterns = new JArray();
        
        // Look at tickets from the last 24 hours
        var recentTickets = tickets.Where(t => 
            t["created_at"] != null && 
            (DateTime.UtcNow - DateTime.Parse(t["created_at"].ToString())).TotalHours <= 24);
        
        if (!recentTickets.Any()) return patterns;
        
        // Analyze for patterns
        var recentCategories = recentTickets
            .Select(t => t["enhanced_context"]?["categorization_suggestions"]?["primary"]?.ToString() ?? "General")
            .GroupBy(c => c)
            .Where(g => g.Count() >= 3); // At least 3 occurrences
        
        foreach (var pattern in recentCategories)
        {
            patterns.Add(new JObject
            {
                ["pattern"] = pattern.Key,
                ["occurrences"] = pattern.Count(),
                ["timeframe"] = "last_24_hours",
                ["significance"] = pattern.Count() >= 5 ? "high" : "medium"
            });
        }
        
        return patterns;
    }
    
    /// <summary>
    /// Analyze workload distribution among agents
    /// </summary>
    private JObject AnalyzeWorkloadDistribution(JArray tickets)
    {
        var workload = new JObject();
        
        // Group by agent - using safe integer parsing
        var ticketsByAgent = tickets
            .Where(t => t["responder_id"] != null && SafeGetInt(t["responder_id"]).HasValue)
            .GroupBy(t => SafeGetInt(t["responder_id"]).Value);
        
        var distribution = new JArray();
        foreach (var agentGroup in ticketsByAgent)
        {
            var agentTickets = agentGroup.ToList();
            distribution.Add(new JObject
            {
                ["agent_id"] = agentGroup.Key,
                ["ticket_count"] = agentTickets.Count,
                ["urgent_count"] = agentTickets.Count(t => SafeGetInt(t["priority"]) == 4),
                ["average_age_hours"] = agentTickets.Any() ? 
                    Math.Round(agentTickets.Average(t => SafeGetIntOrDefault(t["age_hours"])), 1) : 0
            });
        }
        
        workload["agent_distribution"] = distribution;
        workload["unassigned_count"] = tickets.Count(t => t["responder_id"] == null);
        workload["workload_balance"] = CalculateWorkloadBalance(distribution);
        
        return workload;
    }
    
    /// <summary>
    /// Calculate workload balance score
    /// </summary>
    private string CalculateWorkloadBalance(JArray distribution)
    {
        if (distribution.Count < 2) return "not_applicable";
        
        var counts = distribution.Select(d => SafeGetIntOrDefault(d["ticket_count"])).ToList();
        var avg = counts.Average();
        var stdDev = Math.Sqrt(counts.Average(v => Math.Pow(v - avg, 2)));
        var coefficientOfVariation = stdDev / avg;
        
        if (coefficientOfVariation < 0.2) return "well_balanced";
        if (coefficientOfVariation < 0.5) return "moderately_balanced";
        
        return "imbalanced";
    }
    
    /// <summary>
    /// Generate executive summary with natural language and emoji indicators
    /// </summary>
    private string GenerateExecutiveSummary(JObject insights)
    {
        var metrics = insights["metrics"];
        var risks = insights["risks"];
        var trends = insights["trends"];
        
        var summary = new List<string>();
        
        // Volume statement
        var total = metrics["total_count"]?.Value<int>() ?? 0;
        var avgAge = metrics["average_age_hours"]?.Value<double>() ?? 0;
        summary.Add($"📊 Currently managing {total} tickets with an average age of {avgAge:F1} hours.");
        
        // Risk statement
        var slaBreach = risks["sla_breach_count"]?.Value<int>() ?? 0;
        var overdue = risks["overdue_count"]?.Value<int>() ?? 0;
        if (slaBreach > 0 || overdue > 0)
            summary.Add($"⚠️ {slaBreach + overdue} tickets need immediate attention ({slaBreach} approaching SLA, {overdue} overdue).");
        
        // Trend statement
        var volumeTrend = trends["volume_trend"]?.ToString();
        switch (volumeTrend)
        {
            case "increasing_significantly":
                summary.Add("📈 Ticket volume is increasing significantly - consider additional resources.");
                break;
            case "increasing":
                summary.Add("📊 Ticket volume is trending upward - monitor for resource needs.");
                break;
            case "decreasing":
                summary.Add("📉 Ticket volume is decreasing - good progress on backlog.");
                break;
        }
        
        // Critical issues
        var unassignedUrgent = risks["unassigned_urgent"]?.Value<int>() ?? 0;
        if (unassignedUrgent > 0)
            summary.Add($"🚨 Action required: {unassignedUrgent} urgent tickets await assignment.");
        
        var criticalVip = risks["critical_vip_issues"]?.Value<int>() ?? 0;
        if (criticalVip > 0)
            summary.Add($"⭐ {criticalVip} high-priority VIP issues require attention.");
        
        // Performance insight
        var resolutionTrend = trends["resolution_rate_trend"]?.ToString();
        if (resolutionTrend == "excellent")
            summary.Add("✅ Excellent resolution rate maintained.");
        else if (resolutionTrend == "poor")
            summary.Add("⚡ Resolution rate needs improvement - consider process optimization.");
        
        return string.Join(" ", summary);
    }
    
    /// <summary>
    /// Generate actionable recommendations based on insights
    /// </summary>
    private JArray GenerateActionableRecommendations(JObject insights, JArray tickets)
    {
        var recommendations = new JArray();
        var risks = insights["risks"];
        var workload = insights["workload"];
        var trends = insights["trends"];
        
        // Assignment recommendations
        var unassignedUrgent = risks["unassigned_urgent"]?.Value<int>() ?? 0;
        if (unassignedUrgent > 0)
        {
            recommendations.Add(new JObject
            {
                ["priority"] = "high",
                ["action"] = "Assign urgent tickets",
                ["details"] = $"Immediately assign {unassignedUrgent} unassigned urgent tickets to available agents",
                ["impact"] = "Prevent SLA breaches and improve customer satisfaction"
            });
        }
        
        // Workload balance recommendations
        var balance = workload["workload_balance"]?.ToString();
        if (balance == "imbalanced")
        {
            recommendations.Add(new JObject
            {
                ["priority"] = "medium",
                ["action"] = "Rebalance workload",
                ["details"] = "Redistribute tickets among agents to ensure even workload",
                ["impact"] = "Improve agent productivity and reduce burnout"
            });
        }
        
        // Trend-based recommendations
        var volumeTrend = trends["volume_trend"]?.ToString();
        if (volumeTrend == "increasing_significantly")
        {
            recommendations.Add(new JObject
            {
                ["priority"] = "high",
                ["action"] = "Scale resources",
                ["details"] = "Consider adding temporary staff or automation to handle increased volume",
                ["impact"] = "Maintain service levels despite increased demand"
            });
        }
        
        // Category-specific recommendations
        var commonIssues = trends["common_issues"] as JArray;
        if (commonIssues != null && commonIssues.Count > 0)
        {
            var topIssue = commonIssues[0];
            if (topIssue["percentage"]?.Value<double>() > 30)
            {
                recommendations.Add(new JObject
                {
                    ["priority"] = "medium",
                    ["action"] = "Address common issue",
                    ["details"] = $"Create knowledge base article or automation for '{topIssue["category"]}' issues ({topIssue["percentage"]}% of tickets)",
                    ["impact"] = "Reduce ticket volume and improve self-service"
                });
            }
        }
        
        // Stale ticket recommendations
        var staleWaiting = risks["stale_waiting_customer"]?.Value<int>() ?? 0;
        if (staleWaiting > 0)
        {
            recommendations.Add(new JObject
            {
                ["priority"] = "medium",
                ["action"] = "Follow up on stale tickets",
                ["details"] = $"Contact customers for {staleWaiting} tickets waiting over 48 hours",
                ["impact"] = "Improve resolution times and customer communication"
            });
        }
        
        return recommendations;
    }
    
    /// <summary>
    /// Calculate average resolution time for resolved tickets
    /// </summary>
    private double CalculateAverageResolutionTime(JArray tickets)
    {
        var resolvedTickets = tickets.Where(t => 
            ((t["status"]?.Type == JTokenType.Integer ? t["status"].Value<int?>() : null) == 4 || 
             (t["status"]?.Type == JTokenType.Integer ? t["status"].Value<int?>() : null) == 5) &&
            t["created_at"] != null && t["updated_at"] != null);
        
        if (!resolvedTickets.Any()) return 0;
        
        var resolutionTimes = resolvedTickets.Select(t =>
        {
            var created = DateTime.Parse(t["created_at"].ToString());
            var resolved = DateTime.Parse(t["updated_at"].ToString());
            return (resolved - created).TotalHours;
        });
        
        return Math.Round(resolutionTimes.Average(), 1);
    }
    
    /// <summary>
    /// Calculate first response compliance percentage
    /// </summary>
    private double CalculateFirstResponseCompliance(JArray tickets)
    {
        // This would calculate actual first response compliance if we had the data
        // For now, we'll estimate based on ticket age
        var respondedQuickly = tickets.Count(t => {
            var ageHours = t["age_hours"]?.Type == JTokenType.Integer ? t["age_hours"].Value<int?>() : null;
            var status = t["status"]?.Type == JTokenType.Integer ? t["status"].Value<int?>() : null;
            return (ageHours.HasValue && ageHours < 4) || status != 2;
        });
        
        if (!tickets.Any()) return 0;
        
        return Math.Round((double)respondedQuickly / tickets.Count * 100, 1);
    }
    
    /// <summary>
    /// Calculate reopened ticket rate
    /// </summary>
    private double CalculateReopenedRate(JArray tickets)
    {
        // This would track actual reopened tickets if we had historical data
        // For now, return 0 as we don't have reopened status tracking
        return 0;
    }
    
    /// <summary>
    /// Analyze category distribution for tickets
    /// </summary>
    private JObject AnalyzeCategoryDistribution(JArray tickets)
    {
        var distribution = new JObject();
        var categories = new Dictionary<string, int>();
        
        foreach (var ticket in tickets)
        {
            var category = ticket["enhanced_context"]?["categorization_suggestions"]?["primary"]?.ToString() ?? "General";
            if (categories.ContainsKey(category))
                categories[category]++;
            else
                categories[category] = 1;
        }
        
        var categoryArray = new JArray();
        foreach (var cat in categories.OrderByDescending(kvp => kvp.Value))
        {
            categoryArray.Add(new JObject
            {
                ["category"] = cat.Key,
                ["count"] = cat.Value,
                ["percentage"] = Math.Round((double)cat.Value / tickets.Count * 100, 1)
            });
        }
        
        distribution["categories"] = categoryArray;
        distribution["diversity_score"] = CalculateDiversityScore(categories, tickets.Count);
        
        return distribution;
    }
    
    /// <summary>
    /// Calculate diversity score for category distribution
    /// </summary>
    private double CalculateDiversityScore(Dictionary<string, int> categories, int total)
    {
        if (total == 0) return 0;
        
        // Shannon diversity index
        double sum = 0;
        foreach (var count in categories.Values)
        {
            double proportion = (double)count / total;
            if (proportion > 0)
                sum += proportion * Math.Log(proportion);
        }
        
        return Math.Round(-sum, 2);
    }
    
    /// <summary>
    /// Analyze peak hours for ticket creation
    /// </summary>
    private JArray AnalyzePeakHours(JArray tickets)
    {
        var hourlyDistribution = new Dictionary<int, int>();
        
        foreach (var ticket in tickets.Where(t => t["created_at"] != null))
        {
            var hour = DateTime.Parse(ticket["created_at"].ToString()).Hour;
            if (hourlyDistribution.ContainsKey(hour))
                hourlyDistribution[hour]++;
            else
                hourlyDistribution[hour] = 1;
        }
        
        var peakHours = new JArray();
        foreach (var hour in hourlyDistribution.OrderByDescending(kvp => kvp.Value).Take(3))
        {
            peakHours.Add(new JObject
            {
                ["hour"] = hour.Key,
                ["count"] = hour.Value,
                ["time_range"] = $"{hour.Key:00}:00 - {hour.Key:00}:59"
            });
        }
        
        return peakHours;
    }
    
    /// <summary>
    /// Analyze day of week distribution
    /// </summary>
    private JObject AnalyzeDayOfWeekDistribution(JArray tickets)
    {
        var distribution = new JObject();
        var dayCount = new Dictionary<DayOfWeek, int>();
        
        foreach (var ticket in tickets.Where(t => t["created_at"] != null))
        {
            var day = DateTime.Parse(ticket["created_at"].ToString()).DayOfWeek;
            if (dayCount.ContainsKey(day))
                dayCount[day]++;
            else
                dayCount[day] = 1;
        }
        
        foreach (var day in dayCount.OrderBy(kvp => kvp.Key))
        {
            distribution[day.Key.ToString()] = day.Value;
        }
        
        return distribution;
    }
    
    /// <summary>
    /// Analyze monthly trends
    /// </summary>
    private JObject AnalyzeMonthlyTrends(JArray tickets)
    {
        var trends = new JObject();
        var currentMonth = tickets.Count(t => 
            t["created_at"] != null && 
            DateTime.Parse(t["created_at"].ToString()).Month == DateTime.UtcNow.Month);
        
        trends["current_month_count"] = currentMonth;
        trends["daily_average"] = Math.Round((double)currentMonth / DateTime.UtcNow.Day, 1);
        trends["projected_month_total"] = Math.Round((double)currentMonth / DateTime.UtcNow.Day * DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month), 0);
        
        return trends;
    }
    
    /// <summary>
    /// Analyze agent performance metrics
    /// </summary>
    private JObject AnalyzeAgentPerformance(JArray tickets)
    {
        var performance = new JObject();
        var agentMetrics = new JArray();
        
        var ticketsByAgent = tickets
            .Where(t => t["responder_id"] != null && t["responder_id"].Type == JTokenType.Integer)
            .GroupBy(t => t["responder_id"].Value<int>());
        
        foreach (var agentGroup in ticketsByAgent)
        {
            var agentTickets = agentGroup.ToList();
            var resolved = agentTickets.Count(t => {
                var status = t["status"]?.Type == JTokenType.Integer ? t["status"].Value<int?>() : null;
                return status == 4 || status == 5;
            });
            
            agentMetrics.Add(new JObject
            {
                ["agent_id"] = agentGroup.Key,
                ["total_tickets"] = agentTickets.Count,
                ["resolved_tickets"] = resolved,
                ["resolution_rate"] = Math.Round((double)resolved / agentTickets.Count * 100, 1),
                ["average_handling_time"] = Math.Round(agentTickets.Average(t => t["age_hours"]?.Type == JTokenType.Integer ? t["age_hours"].Value<int>() : 0), 1)
            });
        }
        
        performance["agent_metrics"] = agentMetrics;
        performance["top_performer"] = agentMetrics
            .OrderByDescending(a => a["resolution_rate"])
            .FirstOrDefault()?["agent_id"];
        
        return performance;
    }
    
    /// <summary>
    /// Analyze VIP customer satisfaction indicators
    /// </summary>
    private JObject AnalyzeVIPSatisfaction(JArray tickets)
    {
        var vipAnalysis = new JObject();
        var vipTickets = tickets.Where(t => t["requester"]?["is_vip"]?.Value<bool>() == true).ToList();
        
        if (!vipTickets.Any())
        {
            vipAnalysis["has_vip_tickets"] = false;
            return vipAnalysis;
        }
        
        vipAnalysis["has_vip_tickets"] = true;
        vipAnalysis["total_vip_tickets"] = vipTickets.Count;
        vipAnalysis["average_vip_age_hours"] = Math.Round(vipTickets.Average(t => t["age_hours"]?.Type == JTokenType.Integer ? t["age_hours"].Value<int>() : 0), 1);
        vipAnalysis["unresolved_vip_tickets"] = vipTickets.Count(t => {
            var status = t["status"]?.Type == JTokenType.Integer ? t["status"].Value<int?>() : null;
            return status != 4 && status != 5;
        });
        vipAnalysis["vip_urgency_rate"] = Math.Round(
            (double)vipTickets.Count(t => (t["priority"]?.Type == JTokenType.Integer ? t["priority"].Value<int?>() : null) >= 3) / vipTickets.Count * 100, 1);
        
        return vipAnalysis;
    }
    
    /// <summary>
    /// Analyze response time by priority
    /// </summary>
    private JObject AnalyzeResponseTimeByPriority(JArray tickets)
    {
        var analysis = new JObject();
        
        for (int priority = 1; priority <= 4; priority++)
        {
            var priorityTickets = tickets.Where(t => t["priority"]?.Type == JTokenType.Integer && t["priority"].Value<int>() == priority).ToList();
            if (priorityTickets.Any())
            {
                analysis[$"priority_{priority}"] = new JObject
                {
                    ["count"] = priorityTickets.Count,
                    ["average_age_hours"] = Math.Round(priorityTickets.Average(t => t["age_hours"]?.Type == JTokenType.Integer ? t["age_hours"].Value<int>() : 0), 1),
                    ["label"] = GetPriorityLabel(priority)
                };
            }
        }
        
        return analysis;
    }
    
    /// <summary>
    /// Analyze escalation patterns
    /// </summary>
    private JObject AnalyzeEscalationPatterns(JArray tickets)
    {
        var patterns = new JObject();
        
        // Count tickets that might need escalation
        var escalationCandidates = tickets.Where(t => {
            var ageHours = t["age_hours"]?.Type == JTokenType.Integer ? t["age_hours"].Value<int?>() ?? 0 : 0;
            var status = t["status"]?.Type == JTokenType.Integer ? t["status"].Value<int?>() : null;
            return ageHours > 24 && status != 4 && status != 5;
        }).ToList();
        
        patterns["escalation_candidates"] = escalationCandidates.Count;
        patterns["escalation_rate"] = tickets.Any() ? 
            Math.Round((double)escalationCandidates.Count / tickets.Count * 100, 1) : 0;
        
        // Identify common escalation categories
        var escalationCategories = new JArray();
        var categoryGroups = escalationCandidates
            .GroupBy(t => t["enhanced_context"]?["categorization_suggestions"]?["primary"]?.ToString() ?? "General")
            .OrderByDescending(g => g.Count())
            .Take(3);
        
        foreach (var group in categoryGroups)
        {
            escalationCategories.Add(new JObject
            {
                ["category"] = group.Key,
                ["count"] = group.Count()
            });
        }
        
        patterns["common_escalation_categories"] = escalationCategories;
        
        return patterns;
    }
    
    /// <summary>
    /// Predict resolution load for the next period
    /// </summary>
    private JObject PredictResolutionLoad(JArray tickets)
    {
        var prediction = new JObject();
        
        // Simple prediction based on current open tickets and average resolution time
        var openTickets = tickets.Count(t => {
            var status = t["status"]?.Type == JTokenType.Integer ? t["status"].Value<int?>() : null;
            return status == 2 || status == 3;
        });
        var avgResolutionHours = CalculateAverageResolutionTime(tickets);
        
        prediction["open_tickets"] = openTickets;
        prediction["expected_resolutions_24h"] = openTickets > 0 && avgResolutionHours > 0 ? 
            Math.Round(openTickets * (24.0 / avgResolutionHours), 0) : 0;
        prediction["expected_resolutions_48h"] = openTickets > 0 && avgResolutionHours > 0 ? 
            Math.Round(openTickets * (48.0 / avgResolutionHours), 0) : 0;
        
        return prediction;
    }
    
    /// <summary>
    /// Calculate overall risk score
    /// </summary>
    private double CalculateOverallRiskScore(JArray tickets)
    {
        if (!tickets.Any()) return 0;
        
        double riskScore = 0;
        
        // Factor in various risk elements
        var overdueRate = (double)tickets.Count(t => IsOverdue(t)) / tickets.Count;
        var unassignedUrgentRate = (double)tickets.Count(t => {
            var priority = t["priority"]?.Type == JTokenType.Integer ? t["priority"].Value<int?>() : null;
            return priority.HasValue && priority >= 3 && t["responder_id"] == null;
        }) / tickets.Count;
        var highAgeRate = (double)tickets.Count(t => {
            var ageHours = t["age_hours"]?.Type == JTokenType.Integer ? t["age_hours"].Value<int?>() ?? 0 : 0;
            return ageHours > 72;
        }) / tickets.Count;
        
        riskScore = (overdueRate * 40) + (unassignedUrgentRate * 30) + (highAgeRate * 30);
        
        return Math.Round(Math.Min(riskScore, 100), 1);
    }
    
    /// <summary>
    /// Estimate resource requirements based on current load
    /// </summary>
    private JObject EstimateResourceRequirements(JArray tickets)
    {
        var requirements = new JObject();
        
        var openTickets = tickets.Count(t => {
            var status = t["status"]?.Type == JTokenType.Integer ? t["status"].Value<int?>() : null;
            return status == 2 || status == 3;
        });
        var avgHandlingTime = tickets.Any() ? tickets.Average(t => t["age_hours"]?.Type == JTokenType.Integer ? t["age_hours"].Value<int>() : 0) : 8;
        
        // Simple estimation: assume 8 hour work day, 6 productive hours
        var agentHoursRequired = (openTickets * avgHandlingTime) / 6;
        
        requirements["estimated_agent_hours"] = Math.Round(agentHoursRequired, 1);
        requirements["recommended_agents"] = Math.Ceiling(agentHoursRequired / 8); // 8 hour shifts
        requirements["overtime_risk"] = agentHoursRequired > 40 ? "high" : agentHoursRequired > 30 ? "medium" : "low";
        
        return requirements;
    }
    
    #endregion
    
    #region V4 Enhanced Insights for Other Entities
    
    /// <summary>
    /// Generate enhanced insights for problems
    /// </summary>
    private JObject GenerateEnhancedProblemInsights(JArray problems)
    {
        var insights = new JObject();
        
        insights["metrics"] = new JObject
        {
            ["total_count"] = problems.Count,
            ["open_problems"] = problems.Count(p => p["status"]?.Type == JTokenType.Integer && p["status"].Value<int>() == 1),
            ["change_requested"] = problems.Count(p => p["status"]?.Type == JTokenType.Integer && p["status"].Value<int>() == 2),
            ["high_impact"] = problems.Count(p => p["impact"]?.Type == JTokenType.Integer && p["impact"].Value<int>() == 3),
            ["critical_priority"] = problems.Count(p => (p["priority"]?.Type == JTokenType.Integer ? p["priority"].Value<int?>() : null) >= 3)
        };
        
        insights["impact_analysis"] = new JObject
        {
            ["affected_services"] = CountAffectedServices(problems),
            ["user_impact_estimate"] = EstimateUserImpact(problems),
            ["business_criticality"] = AssessBusinessCriticality(problems)
        };
        
        insights["resolution_progress"] = new JObject
        {
            ["average_age_days"] = problems.Any() ? 
                problems.Average(p => CalculateAgeDays(p["created_at"]?.ToString())) : 0,
            ["stale_problems"] = problems.Count(p => 
                CalculateAgeDays(p["created_at"]?.ToString()) > 30),
            ["pending_changes"] = problems.Count(p => p["status"]?.Type == JTokenType.Integer && p["status"].Value<int>() == 2)
        };
        
        return insights;
    }
    
    /// <summary>
    /// Generate enhanced insights for changes
    /// </summary>
    private JObject GenerateEnhancedChangeInsights(JArray changes)
    {
        var insights = new JObject();
        
        insights["metrics"] = new JObject
        {
            ["total_count"] = changes.Count,
            ["pending_approval"] = changes.Count(c => c["status"]?.Type == JTokenType.Integer && c["status"].Value<int>() == 2),
            ["approved"] = changes.Count(c => c["status"]?.Type == JTokenType.Integer && c["status"].Value<int>() == 3),
            ["in_progress"] = changes.Count(c => c["status"]?.Type == JTokenType.Integer && c["status"].Value<int>() == 6),
            ["high_risk"] = changes.Count(c => (c["risk"]?.Type == JTokenType.Integer ? c["risk"].Value<int?>() : null) >= 3),
            ["emergency_changes"] = changes.Count(c => c["change_type"]?.Type == JTokenType.Integer && c["change_type"].Value<int>() == 4)
        };
        
        insights["schedule_analysis"] = new JObject
        {
            ["changes_this_week"] = CountChangesInTimeframe(changes, 7),
            ["changes_this_month"] = CountChangesInTimeframe(changes, 30),
            ["weekend_changes"] = CountWeekendChanges(changes),
            ["business_hours_changes"] = CountBusinessHoursChanges(changes)
        };
        
        insights["risk_assessment"] = new JObject
        {
            ["overall_risk_level"] = AssessOverallChangeRisk(changes),
            ["high_risk_concentration"] = CalculateHighRiskConcentration(changes),
            ["emergency_change_rate"] = changes.Any() ? 
                Math.Round((double)changes.Count(c => c["change_type"]?.Type == JTokenType.Integer && c["change_type"].Value<int>() == 4) / changes.Count * 100, 1) : 0
        };
        
        return insights;
    }
    
    /// <summary>
    /// Generate enhanced insights for releases
    /// </summary>
    private JObject GenerateEnhancedReleaseInsights(JArray releases)
    {
        var insights = new JObject();
        
        insights["metrics"] = new JObject
        {
            ["total_count"] = releases.Count,
            ["in_progress"] = releases.Count(r => r["status"]?.Type == JTokenType.Integer && r["status"].Value<int>() == 3),
            ["completed"] = releases.Count(r => r["status"]?.Type == JTokenType.Integer && r["status"].Value<int>() == 5),
            ["on_hold"] = releases.Count(r => r["status"]?.Type == JTokenType.Integer && r["status"].Value<int>() == 2),
            ["major_releases"] = releases.Count(r => r["release_type"]?.Type == JTokenType.Integer && r["release_type"].Value<int>() == 3)
        };
        
        insights["completion_analysis"] = new JObject
        {
            ["completion_rate"] = releases.Any() ? 
                Math.Round((double)releases.Count(r => r["status"]?.Type == JTokenType.Integer && r["status"].Value<int>() == 5) / releases.Count * 100, 1) : 0,
            ["average_release_duration"] = CalculateAverageReleaseDuration(releases),
            ["delayed_releases"] = CountDelayedReleases(releases)
        };
        
        insights["upcoming_releases"] = new JObject
        {
            ["next_7_days"] = CountUpcomingReleases(releases, 7),
            ["next_30_days"] = CountUpcomingReleases(releases, 30),
            ["release_density"] = CalculateReleaseDensity(releases)
        };
        
        return insights;
    }
    
    #endregion
    
    #region Webhook Handling
    
    private bool IsWebhookValidation()
    {
        var headers = this.Context.Request.Headers;
        return headers.Contains("X-Hook-Secret") || 
               headers.Contains("X-Freshservice-Webhook-Validation");
    }
    
    private async Task<HttpResponseMessage> HandleWebhookValidation()
    {
        var validationToken = this.Context.Request.Headers.GetValues("X-Hook-Secret").FirstOrDefault() ??
                            this.Context.Request.Headers.GetValues("X-Freshservice-Webhook-Validation").FirstOrDefault();
        
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-Hook-Secret", validationToken);
        response.Content = new StringContent("");
        
        return response;
    }
    
    private async Task<HttpResponseMessage> HandleWebhookTrigger(string operationId)
    {
        var content = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var webhookData = JObject.Parse(content);
        
        JObject transformedData = null;
        
        switch (operationId.ToLower())
        {
            case "newticket":
                transformedData = TransformNewTicketWebhook(webhookData);
                break;
                
            case "ticketupdated":
                transformedData = TransformTicketUpdatedWebhook(webhookData);
                break;
                
            case "ticketclosed":
                transformedData = TransformTicketClosedWebhook(webhookData);
                break;
                
            case "problemcreated":
                transformedData = TransformProblemCreatedWebhook(webhookData);
                break;
                
            case "changeapproved":
                transformedData = TransformChangeApprovedWebhook(webhookData);
                break;
                
            case "highpriorityticket":
                transformedData = TransformHighPriorityTicketWebhook(webhookData);
                break;
                
            case "slaviolation":
                transformedData = TransformSLAViolationWebhook(webhookData);
                break;
                
            case "agentassigned":
                transformedData = TransformAgentAssignedWebhook(webhookData);
                break;
        }
        
        if (transformedData != null)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = CreateJsonContent(transformedData.ToString());
            return response;
        }
        
        var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest);
        errorResponse.Content = CreateJsonContent(new JObject
        {
            ["error"] = new JObject
            {
                ["message"] = "Failed to process webhook",
                ["operationId"] = operationId
            }
        }.ToString());
        return errorResponse;
    }
    
    private JObject TransformNewTicketWebhook(JObject webhookData)
    {
        var ticket = webhookData["ticket"] ?? webhookData;
        
        return new JObject
        {
            ["ticket_id"] = ticket["id"],
            ["subject"] = ticket["subject"],
            ["requester_id"] = ticket["requester_id"],
            ["status"] = ticket["status"],
            ["priority"] = ticket["priority"],
            ["created_at"] = ticket["created_at"],
            ["workspace_id"] = ticket["workspace_id"]
        };
    }
    
    private JObject TransformTicketUpdatedWebhook(JObject webhookData)
    {
        var ticket = webhookData["ticket"] ?? webhookData;
        var changes = webhookData["changes"] ?? new JObject();
        
        return new JObject
        {
            ["ticket_id"] = ticket["id"],
            ["changes"] = changes,
            ["updated_by"] = webhookData["performed_by"] ?? ticket["updated_by"],
            ["updated_at"] = DateTime.UtcNow.ToString("o")
        };
    }
    
    private JObject TransformTicketClosedWebhook(JObject webhookData)
    {
        var ticket = webhookData["ticket"] ?? webhookData;
        
        return new JObject
        {
            ["ticket_id"] = ticket["id"],
            ["closed_at"] = ticket["closed_at"] ?? DateTime.UtcNow.ToString("o"),
            ["resolved_by"] = ticket["responder_id"],
            ["resolution_notes"] = ticket["resolution_notes"]
        };
    }
    
    private JObject TransformProblemCreatedWebhook(JObject webhookData)
    {
        var problem = webhookData["problem"] ?? webhookData;
        
        return new JObject
        {
            ["problem_id"] = problem["id"],
            ["subject"] = problem["subject"],
            ["impact"] = problem["impact"],
            ["priority"] = problem["priority"],
            ["created_at"] = problem["created_at"]
        };
    }
    
    private JObject TransformChangeApprovedWebhook(JObject webhookData)
    {
        var change = webhookData["change"] ?? webhookData;
        
        return new JObject
        {
            ["change_id"] = change["id"],
            ["approved_by"] = webhookData["approved_by"] ?? change["approval_details"]?["approved_by"],
            ["approved_at"] = webhookData["approved_at"] ?? DateTime.UtcNow.ToString("o"),
            ["change_type"] = change["change_type"],
            ["risk"] = change["risk"]
        };
    }
    
    private JObject TransformHighPriorityTicketWebhook(JObject webhookData)
    {
        var ticket = webhookData["ticket"] ?? webhookData;
        
        return new JObject
        {
            ["ticket_id"] = ticket["id"],
            ["subject"] = ticket["subject"],
            ["requester"] = new JObject
            {
                ["id"] = ticket["requester_id"],
                ["email"] = ticket["requester"]?["email"],
                ["name"] = ticket["requester"]?["name"]
            },
            ["priority"] = ticket["priority"],
            ["urgency"] = ticket["urgency"],
            ["impact"] = ticket["impact"]
        };
    }
    
    private JObject TransformSLAViolationWebhook(JObject webhookData)
    {
        return new JObject
        {
            ["ticket_id"] = webhookData["ticket_id"],
            ["sla_policy"] = webhookData["sla_policy_name"],
            ["violation_time"] = webhookData["violation_time"],
            ["metric"] = webhookData["metric"],
            ["breach_type"] = webhookData["breach_type"]
        };
    }
    
    private JObject TransformAgentAssignedWebhook(JObject webhookData)
    {
        var ticket = webhookData["ticket"] ?? webhookData;
        var changes = webhookData["changes"] ?? new JObject();
        
        return new JObject
        {
            ["ticket_id"] = ticket["id"],
            ["agent_id"] = changes["agent_id"]?["new"] ?? ticket["responder_id"],
            ["previous_agent_id"] = changes["agent_id"]?["old"],
            ["assigned_at"] = DateTime.UtcNow.ToString("o")
        };
    }
    
    #endregion
    
    #region Helper Methods (Continued)
    
    /// <summary>
    /// Safely get integer value from JToken with overflow protection
    /// </summary>
    private int? SafeGetInt(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;
            
        try
        {
            if (token.Type == JTokenType.Integer)
            {
                // Check if the value fits in Int32 range
                var longValue = token.Value<long>();
                if (longValue >= int.MinValue && longValue <= int.MaxValue)
                    return (int)longValue;
                else
                {
                    this.Context.Logger.LogWarning($"Integer value {longValue} exceeds Int32 range, returning null");
                    return null;
                }
            }
            else if (token.Type == JTokenType.Float)
            {
                var doubleValue = token.Value<double>();
                if (doubleValue >= int.MinValue && doubleValue <= int.MaxValue)
                    return (int)doubleValue;
                else
                {
                    this.Context.Logger.LogWarning($"Float value {doubleValue} exceeds Int32 range, returning null");
                    return null;
                }
            }
            else if (token.Type == JTokenType.String)
            {
                if (int.TryParse(token.ToString(), out int result))
                    return result;
            }
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogWarning($"Failed to parse integer from token: {ex.Message}");
        }
        
        return null;
    }
    
    /// <summary>
    /// Safely get integer value with default
    /// </summary>
    private int SafeGetIntOrDefault(JToken token, int defaultValue = 0)
    {
        return SafeGetInt(token) ?? defaultValue;
    }
    
    private double CalculateCategoryRelevance(string content, string category)
    {
        var keywords = GetCategoryKeywords(category);
        var matchCount = keywords.Count(keyword => 
            Regex.IsMatch(content, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase));
        
        return (double)matchCount / keywords.Length;
    }
    
    private string DetectSimilarPatterns(JToken ticket)
    {
        var subject = ticket["subject"]?.ToString().ToLower() ?? "";
        var category = ticket["enhanced_context"]?["categorization_suggestions"]?["primary"]?.ToString() ?? "General";
        
        // Common pattern detection
        var patterns = new Dictionary<string, string>
        {
            ["password.*reset"] = "Common password reset request - Consider self-service portal",
            ["vpn.*connect"] = "VPN connectivity issue - Check network configuration guide",
            ["email.*not.*working"] = "Email service issue - Verify server status",
            ["slow.*computer|computer.*slow"] = "Performance issue - May need hardware upgrade",
            ["access.*denied|permission.*denied"] = "Access issue - Verify permissions and group membership",
            ["printer.*not.*working|can.*print"] = "Printing issue - Check printer queue and drivers",
            ["install.*software|software.*install"] = "Software installation request - Check approved software list",
            ["disk.*full|storage.*full"] = "Storage issue - Consider cleanup or expansion"
        };
        
        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(subject, pattern.Key, RegexOptions.IgnoreCase))
            {
                return pattern.Value;
            }
        }
        
        return $"Unique issue in {category} category - Requires individual analysis";
    }
    
    private string EstimateResolutionTime(JToken ticket)
    {
        var priority = ticket["priority"]?.Type == JTokenType.Integer ? ticket["priority"].Value<int?>() ?? 1 : 1;
        var category = ticket["enhanced_context"]?["categorization_suggestions"]?["primary"]?.ToString() ?? "General";
        var isVip = ticket["requester"]?["is_vip"]?.Value<bool>() ?? false;
        
        // Base estimates by category
        var categoryEstimates = new Dictionary<string, int>
        {
            ["Password Reset"] = 30,
            ["Software Installation"] = 120,
            ["Hardware"] = 240,
            ["Network"] = 180,
            ["Email/Collaboration"] = 90,
            ["Access Management"] = 60,
            ["Security"] = 360,
            ["General"] = 120
        };
        
        var baseMinutes = categoryEstimates.ContainsKey(category) ? categoryEstimates[category] : 120;
        
        // Adjust for priority
        switch (priority)
        {
            case 4: // Urgent
                baseMinutes = (int)(baseMinutes * 0.5);
                break;
            case 3: // High
                baseMinutes = (int)(baseMinutes * 0.75);
                break;
            case 1: // Low
                baseMinutes = (int)(baseMinutes * 1.5);
                break;
        }
        
        // VIP adjustment
        if (isVip)
            baseMinutes = (int)(baseMinutes * 0.75);
        
        // Format the estimate
        if (baseMinutes < 60)
            return $"Within {baseMinutes} minutes";
        else if (baseMinutes < 480) // 8 hours
            return $"Within {baseMinutes / 60} hours";
        else if (baseMinutes < 1440) // 24 hours
            return "Within 1 business day";
        else
            return $"Within {baseMinutes / 480} business days";
    }
    
    private JArray IdentifyRequiredSkills(JToken ticket)
    {
        var skills = new JArray();
        var category = ticket["enhanced_context"]?["categorization_suggestions"]?["primary"]?.ToString() ?? "General";
        var subcategory = ticket["enhanced_context"]?["categorization_suggestions"]?["subcategory"]?.ToString() ?? "";
        var isVip = ticket["requester"]?["is_vip"]?.Value<bool>() ?? false;
        
        // Category-based skills
        var categorySkills = new Dictionary<string, string[]>
        {
            ["Hardware"] = new[] { "Hardware Troubleshooting", "Desktop Support" },
            ["Network"] = new[] { "Network Engineering", "TCP/IP", "Routing & Switching" },
            ["Software"] = new[] { "Application Support", "Software Deployment" },
            ["Access Management"] = new[] { "Active Directory", "Identity Management" },
            ["Email/Collaboration"] = new[] { "Exchange Administration", "Office 365" },
            ["Security"] = new[] { "Security Operations", "Incident Response" },
            ["Database"] = new[] { "Database Administration", "SQL" },
            ["Server/Infrastructure"] = new[] { "Server Administration", "Virtualization" }
        };
        
        // Add category skills
        if (categorySkills.ContainsKey(category))
        {
            foreach (var skill in categorySkills[category])
            {
                skills.Add(skill);
            }
        }
        
        // Add subcategory-specific skills
        if (subcategory == "VPN")
            skills.Add("VPN Configuration");
        if (subcategory == "Performance")
            skills.Add("Performance Tuning");
        
        // Add VIP skill requirement
        if (isVip)
            skills.Add("VIP Customer Service");
        
        // Add priority-based skills
        var priority = ticket["priority"]?.Type == JTokenType.Integer ? ticket["priority"].Value<int?>() ?? 1 : 1;
        if (priority >= 3)
            skills.Add("Incident Management");
        
        return skills;
    }
    
    /// <summary>
    /// Get request with transformed natural language query if applicable
    /// </summary>
    private async Task<HttpRequestMessage> GetRequestWithTransformedQuery(string operationId)
    {
        // Only transform for list operations
        if (!operationId.StartsWith("List", StringComparison.OrdinalIgnoreCase))
            return this.Context.Request;
        
        var uri = this.Context.Request.RequestUri;
        var query = HttpUtility.ParseQueryString(uri.Query);
        
        // Check for natural language query parameter
        var naturalQuery = query["natural_query"] ?? query["nlq"];
        if (string.IsNullOrEmpty(naturalQuery))
            return this.Context.Request;
        
        // Decode the query
        naturalQuery = HttpUtility.UrlDecode(naturalQuery);
        
        // Transform to API filters
        var apiFilter = TranslateNaturalLanguageQuery(naturalQuery);
        
        if (!string.IsNullOrEmpty(apiFilter))
        {
            // Add or update the query parameter
            query["query"] = apiFilter;
            
            // Rebuild the URI with the modified query
            var uriBuilder = new UriBuilder(uri)
            {
                Query = query.ToString()
            };
            
            // Create a new request with the modified URI
            var newRequest = new HttpRequestMessage(this.Context.Request.Method, uriBuilder.Uri);
            
            // Copy headers
            foreach (var header in this.Context.Request.Headers)
            {
                newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            
            // Copy content if present
            if (this.Context.Request.Content != null)
            {
                var content = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
                newRequest.Content = new StringContent(content, Encoding.UTF8, "application/json");
            }
            
            return newRequest;
        }
        
        return this.Context.Request;
    }
    
    /// <summary>
    /// Generate ticket list summary
    /// </summary>
    private JObject GenerateTicketListSummary(JArray tickets)
    {
        var summary = new JObject
        {
            ["total_tickets"] = tickets.Count,
            ["open_tickets"] = tickets.Count(t => t["status"]?.Type == JTokenType.Integer && t["status"].Value<int>() == 2),
            ["urgent_tickets"] = tickets.Count(t => t["priority"]?.Type == JTokenType.Integer && t["priority"].Value<int>() == 4),
            ["overdue_tickets"] = tickets.Count(t => IsOverdue(t))
        };
        
        // Natural language summary
        var parts = new List<string>();
        parts.Add($"You have {summary["total_tickets"]} tickets");
        
        if (summary["open_tickets"]?.Value<int>() > 0)
            parts.Add($"{summary["open_tickets"]} open");
        
        if (summary["urgent_tickets"]?.Value<int>() > 0)
            parts.Add($"{summary["urgent_tickets"]} urgent");
        
        if (summary["overdue_tickets"]?.Value<int>() > 0)
            parts.Add($"{summary["overdue_tickets"]} overdue");
        
        summary["natural_language"] = parts.Count > 1 ? 
            parts[0] + ": " + string.Join(", ", parts.Skip(1)) + "." : 
            parts[0] + ".";
        
        return summary;
    }
    
    /// <summary>
    /// Check if ticket is overdue
    /// </summary>
    private bool IsOverdue(JToken ticket)
    {
        var dueBy = ticket["due_by"]?.ToString();
        if (string.IsNullOrEmpty(dueBy))
            return false;
        
        try
        {
            var dueDate = DateTime.Parse(dueBy);
            return dueDate < DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Check if ticket is approaching SLA
    /// </summary>
    private bool IsApproachingSLA(JToken ticket)
    {
        var dueBy = ticket["due_by"]?.ToString();
        if (string.IsNullOrEmpty(dueBy))
            return false;
        
        try
        {
            var dueDate = DateTime.Parse(dueBy);
            var hoursUntilDue = (dueDate - DateTime.UtcNow).TotalHours;
            return hoursUntilDue > 0 && hoursUntilDue < 4; // Within 4 hours
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Copy headers from one response to another
    /// </summary>
    private void CopyHeaders(HttpResponseMessage source, HttpResponseMessage destination)
    {
        foreach (var header in source.Headers)
        {
            destination.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        
        if (source.Content != null)
        {
            foreach (var header in source.Content.Headers)
            {
                destination.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }
    
    // Additional helper methods for other insights
    
    private int CountAffectedServices(JArray problems)
    {
        // This would count unique affected services if the data was available
        // For now, estimate based on impact level
        return problems.Count(p => (p["impact"]?.Type == JTokenType.Integer ? p["impact"].Value<int?>() : null) >= 2);
    }
    
    private int EstimateUserImpact(JArray problems)
    {
        // Estimate users affected based on impact levels
        var highImpact = problems.Count(p => p["impact"]?.Type == JTokenType.Integer && p["impact"].Value<int>() == 3);
        var mediumImpact = problems.Count(p => p["impact"]?.Type == JTokenType.Integer && p["impact"].Value<int>() == 2);
        
        return (highImpact * 100) + (mediumImpact * 25);
    }
    
    private string AssessBusinessCriticality(JArray problems)
    {
        var criticalProblems = problems.Count(p => {
            var priority = p["priority"]?.Type == JTokenType.Integer ? p["priority"].Value<int?>() : null;
            var impact = p["impact"]?.Type == JTokenType.Integer ? p["impact"].Value<int?>() : null;
            return priority >= 3 && impact >= 3;
        });
        
        if (criticalProblems >= 3) return "Critical";
        if (criticalProblems >= 1) return "High";
        if (problems.Any(p => (p["priority"]?.Type == JTokenType.Integer ? p["priority"].Value<int?>() : null) >= 3)) return "Medium";
        
        return "Low";
    }
    
    private double CalculateAgeDays(string createdAt)
    {
        if (string.IsNullOrEmpty(createdAt)) return 0;
        
        try
        {
            var created = DateTime.Parse(createdAt);
            return (DateTime.UtcNow - created).TotalDays;
        }
        catch
        {
            return 0;
        }
    }
    
    private int CountChangesInTimeframe(JArray changes, int days)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(days);
        
        return changes.Count(c =>
        {
            var plannedStart = c["planned_start_date"]?.ToString();
            if (string.IsNullOrEmpty(plannedStart)) return false;
            
            try
            {
                var startDate = DateTime.Parse(plannedStart);
                return startDate >= DateTime.UtcNow && startDate <= cutoffDate;
            }
            catch
            {
                return false;
            }
        });
    }
    
    private int CountWeekendChanges(JArray changes)
    {
        return changes.Count(c =>
        {
            var plannedStart = c["planned_start_date"]?.ToString();
            if (string.IsNullOrEmpty(plannedStart)) return false;
            
            try
            {
                var startDate = DateTime.Parse(plannedStart);
                return startDate.DayOfWeek == DayOfWeek.Saturday || 
                       startDate.DayOfWeek == DayOfWeek.Sunday;
            }
            catch
            {
                return false;
            }
        });
    }
    
    private int CountBusinessHoursChanges(JArray changes)
    {
        return changes.Count(c =>
        {
            var plannedStart = c["planned_start_date"]?.ToString();
            if (string.IsNullOrEmpty(plannedStart)) return false;
            
            try
            {
                var startDate = DateTime.Parse(plannedStart);
                return startDate.DayOfWeek != DayOfWeek.Saturday && 
                       startDate.DayOfWeek != DayOfWeek.Sunday &&
                       startDate.Hour >= 9 && startDate.Hour < 17;
            }
            catch
            {
                return false;
            }
        });
    }
    
    private string AssessOverallChangeRisk(JArray changes)
    {
        if (!changes.Any()) return "None";
        
        var highRiskCount = changes.Count(c => (c["risk"]?.Type == JTokenType.Integer ? c["risk"].Value<int?>() : null) >= 3);
        var emergencyCount = changes.Count(c => c["change_type"]?.Type == JTokenType.Integer && c["change_type"].Value<int>() == 4);
        
        if (highRiskCount >= 3 || emergencyCount >= 2) return "Critical";
        if (highRiskCount >= 1 || emergencyCount >= 1) return "High";
        if (changes.Count(c => c["risk"]?.Type == JTokenType.Integer && c["risk"].Value<int>() == 2) >= 3) return "Medium";
        
        return "Low";
    }
    
    private double CalculateHighRiskConcentration(JArray changes)
    {
        if (!changes.Any()) return 0;
        
        var highRiskCount = changes.Count(c => (c["risk"]?.Type == JTokenType.Integer ? c["risk"].Value<int?>() : null) >= 3);
        return Math.Round((double)highRiskCount / changes.Count * 100, 1);
    }
    
    private double CalculateAverageReleaseDuration(JArray releases)
    {
        var completedReleases = releases.Where(r => 
            r["status"]?.Type == JTokenType.Integer && r["status"].Value<int>() == 5 &&
            r["created_at"] != null && 
            r["updated_at"] != null);
        
        if (!completedReleases.Any()) return 0;
        
        var durations = completedReleases.Select(r =>
        {
            var created = DateTime.Parse(r["created_at"].ToString());
            var completed = DateTime.Parse(r["updated_at"].ToString());
            return (completed - created).TotalDays;
        });
        
        return Math.Round(durations.Average(), 1);
    }
    
    private int CountDelayedReleases(JArray releases)
    {
        return releases.Count(r =>
        {
            var plannedEnd = r["planned_end_date"]?.ToString();
            var status = r["status"]?.Type == JTokenType.Integer ? r["status"].Value<int?>() ?? 0 : 0;
            
            if (string.IsNullOrEmpty(plannedEnd) || status == 5) return false;
            
            try
            {
                var endDate = DateTime.Parse(plannedEnd);
                return endDate < DateTime.UtcNow;
            }
            catch
            {
                return false;
            }
        });
    }
    
    private int CountUpcomingReleases(JArray releases, int days)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(days);
        
        return releases.Count(r =>
        {
            var plannedStart = r["planned_start_date"]?.ToString();
            var status = r["status"]?.Type == JTokenType.Integer ? r["status"].Value<int?>() ?? 0 : 0;
            
            if (string.IsNullOrEmpty(plannedStart) || status == 5) return false;
            
            try
            {
                var startDate = DateTime.Parse(plannedStart);
                return startDate >= DateTime.UtcNow && startDate <= cutoffDate;
            }
            catch
            {
                return false;
            }
        });
    }
    
    private string CalculateReleaseDensity(JArray releases)
    {
        var next30Days = CountUpcomingReleases(releases, 30);
        
        if (next30Days >= 10) return "High - Multiple releases scheduled";
        if (next30Days >= 5) return "Medium - Several releases planned";
        if (next30Days >= 1) return "Low - Few releases scheduled";
        
        return "None - No upcoming releases";
    }
    
    #endregion
    
    #region Missing Helper Methods for Ticket/Problem/Change/Release Enrichment
    
    /// <summary>
    /// Enrich ticket data with calculated fields and labels
    /// </summary>
    private void EnrichTicketData(JToken ticket)
    {
        // Calculate age in hours and days
        if (ticket["created_at"] != null)
        {
            var createdAt = DateTime.Parse(ticket["created_at"].ToString());
            var age = DateTime.UtcNow - createdAt;
            ticket["age_hours"] = (int)age.TotalHours;
            ticket["age_days"] = (int)age.TotalDays;
            ticket["age_formatted"] = FormatAge(age);
        }
        
        // Add priority label
        var priority = ticket["priority"]?.Type == JTokenType.Integer ? ticket["priority"].Value<int?>() ?? 1 : 1;
        ticket["priority_label"] = GetPriorityLabel(priority);
        
        // Add status label
        var status = ticket["status"]?.Type == JTokenType.Integer ? ticket["status"].Value<int?>() ?? 2 : 2;
        ticket["status_label"] = GetStatusLabel(status);
        
        // Add source label
        var source = ticket["source"]?.Type == JTokenType.Integer ? ticket["source"].Value<int?>() ?? 1 : 1;
        ticket["source_label"] = GetSourceLabel(source);
    }
    
    /// <summary>
    /// Add semantic context to make ticket data more AI/Copilot friendly
    /// </summary>
    private void AddTicketSemanticContext(JToken ticket)
    {
        var semanticContext = new JObject();
        
        // Determine urgency level
        var priority = ticket["priority"]?.Type == JTokenType.Integer ? ticket["priority"].Value<int?>() ?? 1 : 1;
        var ageHours = ticket["age_hours"]?.Type == JTokenType.Integer ? ticket["age_hours"].Value<int?>() ?? 0 : 0;
        
        if (priority == 4 || (priority == 3 && ageHours > 4))
            semanticContext["urgency_level"] = "Critical - Immediate attention required";
        else if (priority == 3 || (priority == 2 && ageHours > 24))
            semanticContext["urgency_level"] = "High - Priority attention needed";
        else if (priority == 2 || ageHours > 48)
            semanticContext["urgency_level"] = "Medium - Standard priority";
        else
            semanticContext["urgency_level"] = "Low - Can be scheduled";
        
        // Natural language status
        var status = ticket["status"]?.Type == JTokenType.Integer ? ticket["status"].Value<int?>() ?? 2 : 2;
        switch (status)
        {
            case 2:
                semanticContext["natural_language_status"] = "This ticket is open and awaiting action";
                break;
            case 3:
                semanticContext["natural_language_status"] = "This ticket is pending and may require follow-up";
                break;
            case 4:
                semanticContext["natural_language_status"] = "This ticket has been resolved";
                break;
            case 5:
                semanticContext["natural_language_status"] = "This ticket is closed";
                break;
            case 6:
                semanticContext["natural_language_status"] = "Waiting for customer response";
                break;
            default:
                semanticContext["natural_language_status"] = "Status unknown";
                break;
        }
        
        // Action required flag
        semanticContext["requires_action"] = (status == 2 || status == 3) && ticket["responder_id"] != null;
        
        ticket["semantic_context"] = semanticContext;
        
        // Add enhanced context
        var enhancedContext = new JObject();
        
        // Suggested actions
        var suggestedActions = new JArray();
        if (status == 2 && ticket["responder_id"] == null)
            suggestedActions.Add("Assign to appropriate agent");
        if (priority >= 3 && ageHours > 2)
            suggestedActions.Add("Escalate to senior technician");
        if (status == 6 && ageHours > 48)
            suggestedActions.Add("Follow up with customer");
        
        enhancedContext["suggested_actions"] = suggestedActions;
        
        // Escalation risk
        if (priority >= 3 && ageHours > 4)
            enhancedContext["escalation_risk"] = "High";
        else if (ageHours > 24)
            enhancedContext["escalation_risk"] = "Medium";
        else
            enhancedContext["escalation_risk"] = "Low";
        
        // Customer sentiment (simplified - would use NLP in production)
        var description = ticket["description_text"]?.ToString() ?? "";
        if ((description != null && description.IndexOf("urgent", StringComparison.OrdinalIgnoreCase) >= 0) ||
        (description != null && description.IndexOf("asap", StringComparison.OrdinalIgnoreCase) >= 0) ||
        (description != null && description.IndexOf("critical", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            enhancedContext["customer_sentiment"] = "Urgent/Frustrated";
        }
        else if ((description != null && description.IndexOf("please", StringComparison.OrdinalIgnoreCase) >= 0) ||
                 (description != null && description.IndexOf("thank", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            enhancedContext["customer_sentiment"] = "Polite/Patient";
        }
        else
        {
            enhancedContext["customer_sentiment"] = "Neutral";
        }

        
        // Intelligent categorization
        var categorization = CategorizeTicket(ticket);
        enhancedContext["categorization_suggestions"] = categorization;
        
        // Pattern detection
        enhancedContext["similar_tickets_pattern"] = DetectSimilarPatterns(ticket);
        
        // Resolution time estimate
        enhancedContext["resolution_time_estimate"] = EstimateResolutionTime(ticket);
        
        // Required skills
        enhancedContext["skill_requirements"] = IdentifyRequiredSkills(ticket);
        
        ticket["enhanced_context"] = enhancedContext;
    }
    
    /// <summary>
    /// Add workflow hints for automation
    /// </summary>
    private void AddTicketWorkflowHints(JToken ticket)
    {
        var workflowHints = new JObject();
        
        var status = ticket["status"]?.Type == JTokenType.Integer ? ticket["status"].Value<int?>() ?? 2 : 2;
        var priority = ticket["priority"]?.Type == JTokenType.Integer ? ticket["priority"].Value<int?>() ?? 1 : 1;
        var ageHours = ticket["age_hours"]?.Type == JTokenType.Integer ? ticket["age_hours"].Value<int?>() ?? 0 : 0;
        
        // Next best action
        if (status == 2 && ticket["responder_id"] == null)
            workflowHints["next_action"] = "assign_agent";
        else if (status == 2 && ageHours > 1)
            workflowHints["next_action"] = "send_first_response";
        else if (status == 3 && ageHours > 24)
            workflowHints["next_action"] = "follow_up";
        else if (status == 6 && ageHours > 48)
            workflowHints["next_action"] = "escalate_or_close";
        
        // Automation eligible
        var category = ticket["enhanced_context"]?["categorization_suggestions"]?["primary"]?.ToString() ?? "";
        workflowHints["automation_eligible"] = 
            category == "Password Reset" || 
            category == "Account Unlock" || 
            category == "Software Installation";
        
        // SLA status
        if (ticket["due_by"] != null)
        {
            var dueBy = DateTime.Parse(ticket["due_by"].ToString());
            var hoursUntilDue = (dueBy - DateTime.UtcNow).TotalHours;
            
            if (hoursUntilDue < 0)
                workflowHints["sla_status"] = "breached";
            else if (hoursUntilDue < 2)
                workflowHints["sla_status"] = "at_risk";
            else
                workflowHints["sla_status"] = "on_track";
        }
        
        ticket["workflow_context"] = workflowHints;
    }
    
    /// <summary>
    /// Enrich problem data with labels and calculated fields
    /// </summary>
    private void EnrichProblemData(JToken problem)
    {
        // Add impact label
        var impact = problem["impact"]?.Type == JTokenType.Integer ? problem["impact"].Value<int?>() ?? 1 : 1;
        problem["impact_label"] = GetImpactLabel(impact);
        
        // Add status label
        var status = problem["status"]?.Type == JTokenType.Integer ? problem["status"].Value<int?>() ?? 1 : 1;
        problem["status_label"] = GetProblemStatusLabel(status);
        
        // Add priority label
        var priority = problem["priority"]?.Type == JTokenType.Integer ? problem["priority"].Value<int?>() ?? 1 : 1;
        problem["priority_label"] = GetPriorityLabel(priority);
    }
    
    /// <summary>
    /// Enrich change data with labels and calculated fields
    /// </summary>
    private void EnrichChangeData(JToken change)
    {
        // Add risk label
        var risk = change["risk"]?.Type == JTokenType.Integer ? change["risk"].Value<int?>() ?? 1 : 1;
        change["risk_label"] = GetRiskLabel(risk);
        
        // Add change type label
        var changeType = change["change_type"]?.Type == JTokenType.Integer ? change["change_type"].Value<int?>() ?? 1 : 1;
        change["change_type_label"] = GetChangeTypeLabel(changeType);
        
        // Add impact label
        var impact = change["impact"]?.Type == JTokenType.Integer ? change["impact"].Value<int?>() ?? 1 : 1;
        change["impact_label"] = GetImpactLabel(impact);
        
        // Add status label
        var status = change["status"]?.Type == JTokenType.Integer ? change["status"].Value<int?>() ?? 1 : 1;
        change["status_label"] = GetChangeStatusLabel(status);
        
        // Calculate hours to implementation
        if (change["planned_start_date"] != null)
        {
            var plannedStart = DateTime.Parse(change["planned_start_date"].ToString());
            var hoursToStart = (plannedStart - DateTime.UtcNow).TotalHours;
            change["hours_to_implementation"] = Math.Max(0, (int)hoursToStart);
        }
    }
    
    /// <summary>
    /// Enrich release data with labels and calculated fields
    /// </summary>
    private void EnrichReleaseData(JToken release)
    {
        // Add status label
        var status = release["status"]?.Type == JTokenType.Integer ? release["status"].Value<int?>() ?? 1 : 1;
        release["status_label"] = GetReleaseStatusLabel(status);
        
        // Add priority label
        var priority = release["priority"]?.Type == JTokenType.Integer ? release["priority"].Value<int?>() ?? 1 : 1;
        release["priority_label"] = GetPriorityLabel(priority);
        
        // Add release type label
        var releaseType = release["release_type"]?.Type == JTokenType.Integer ? release["release_type"].Value<int?>() ?? 1 : 1;
        release["release_type_label"] = GetReleaseTypeLabel(releaseType);
    }
    
    /// <summary>
    /// Format time spent in a human-readable format
    /// </summary>
    private string FormatTimeSpent(int totalMinutes)
    {
        if (totalMinutes < 60)
            return $"{totalMinutes}m";
        
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        
        if (minutes == 0)
            return $"{hours}h";
        else
            return $"{hours}h {minutes}m";
    }
    
    /// <summary>
    /// Format age timespan in a human-readable format
    /// </summary>
    private string FormatAge(TimeSpan age)
    {
        if (age.TotalDays >= 1)
            return $"{(int)age.TotalDays}d {age.Hours}h";
        else if (age.TotalHours >= 1)
            return $"{(int)age.TotalHours}h {age.Minutes}m";
        else
            return $"{(int)age.TotalMinutes}m";
    }
    
    /// <summary>
    /// Get priority label from priority code
    /// </summary>
    private string GetPriorityLabel(int priority)
    {
        switch (priority)
        {
            case 1: return "Low";
            case 2: return "Medium";
            case 3: return "High";
            case 4: return "Urgent";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get status label from status code
    /// </summary>
    private string GetStatusLabel(int status)
    {
        switch (status)
        {
            case 2: return "Open";
            case 3: return "Pending";
            case 4: return "Resolved";
            case 5: return "Closed";
            case 6: return "Waiting on Customer";
            case 7: return "Waiting on Third Party";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get source label from source code
    /// </summary>
    private string GetSourceLabel(int source)
    {
        switch (source)
        {
            case 1: return "Email";
            case 2: return "Portal";
            case 3: return "Phone";
            case 4: return "Chat";
            case 5: return "Feedback Widget";
            case 6: return "Yammer";
            case 7: return "AWS Cloudwatch";
            case 8: return "Pagerduty";
            case 9: return "Walkup";
            case 10: return "Slack";
            default: return "Other";
        }
    }
    
    /// <summary>
    /// Get impact label from impact code
    /// </summary>
    private string GetImpactLabel(int impact)
    {
        switch (impact)
        {
            case 1: return "Low";
            case 2: return "Medium";
            case 3: return "High";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get problem status label
    /// </summary>
    private string GetProblemStatusLabel(int status)
    {
        switch (status)
        {
            case 1: return "Open";
            case 2: return "Change Requested";
            case 3: return "Closed";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get risk label from risk code
    /// </summary>
    private string GetRiskLabel(int risk)
    {
        switch (risk)
        {
            case 1: return "Low";
            case 2: return "Medium";
            case 3: return "High";
            case 4: return "Very High";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get change type label
    /// </summary>
    private string GetChangeTypeLabel(int changeType)
    {
        switch (changeType)
        {
            case 1: return "Minor";
            case 2: return "Standard";
            case 3: return "Major";
            case 4: return "Emergency";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get change status label
    /// </summary>
    private string GetChangeStatusLabel(int status)
    {
        switch (status)
        {
            case 1: return "Open";
            case 2: return "Planning";
            case 3: return "Approval";
            case 4: return "Pending Release";
            case 5: return "Pending Review";
            case 6: return "Closed";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get release status label
    /// </summary>
    private string GetReleaseStatusLabel(int status)
    {
        switch (status)
        {
            case 1: return "Open";
            case 2: return "On Hold";
            case 3: return "In Progress";
            case 4: return "Incomplete";
            case 5: return "Completed";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get release type label
    /// </summary>
    private string GetReleaseTypeLabel(int releaseType)
    {
        switch (releaseType)
        {
            case 1: return "Minor";
            case 2: return "Standard";
            case 3: return "Major";
            case 4: return "Emergency";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Categorize ticket based on content analysis
    /// </summary>
    private JObject CategorizeTicket(JToken ticket)
    {
        var categorization = new JObject();
        var subject = ticket["subject"]?.ToString().ToLower() ?? "";
        var description = ticket["description_text"]?.ToString().ToLower() ?? "";
        var content = subject + " " + description;
        
        // Define category patterns
        var categories = new Dictionary<string, double>
        {
            ["Hardware"] = CalculateCategoryRelevance(content, "Hardware"),
            ["Software"] = CalculateCategoryRelevance(content, "Software"),
            ["Network"] = CalculateCategoryRelevance(content, "Network"),
            ["Access Management"] = CalculateCategoryRelevance(content, "Access Management"),
            ["Email/Collaboration"] = CalculateCategoryRelevance(content, "Email/Collaboration"),
            ["Password Reset"] = CalculateCategoryRelevance(content, "Password Reset"),
            ["Security"] = CalculateCategoryRelevance(content, "Security"),
            ["Database"] = CalculateCategoryRelevance(content, "Database"),
            ["Server/Infrastructure"] = CalculateCategoryRelevance(content, "Server/Infrastructure")
        };
        
        // Find primary category
        var primaryCategory = categories.OrderByDescending(kvp => kvp.Value).First();
        categorization["primary"] = primaryCategory.Value > 0.1 ? primaryCategory.Key : "General";
        categorization["confidence"] = Math.Min(95, primaryCategory.Value * 100);
        
        // Find secondary categories
        var secondaryCategories = categories
            .Where(kvp => kvp.Value > 0.05 && kvp.Key != primaryCategory.Key)
            .OrderByDescending(kvp => kvp.Value)
            .Take(3)
            .Select(kvp => kvp.Key);
        
        categorization["secondary"] = new JArray(secondaryCategories);
        
        // Suggested tags based on content
        var tags = new JArray();
        if (content.Contains("urgent") || content.Contains("asap")) tags.Add("urgent");
        if (content.Contains("vip") || ticket["requester"]?["is_vip"]?.Value<bool>() == true) tags.Add("vip");
        if (content.Contains("outage") || content.Contains("down")) tags.Add("outage");
        if (content.Contains("slow") || content.Contains("performance")) tags.Add("performance");
        
        categorization["suggested_tags"] = tags;
        
        // Detect subcategory
        if (primaryCategory.Key == "Network")
        {
            if (content.Contains("vpn")) categorization["subcategory"] = "VPN";
            else if (content.Contains("wifi") || content.Contains("wireless")) categorization["subcategory"] = "WiFi";
            else if (content.Contains("firewall")) categorization["subcategory"] = "Firewall";
        }
        else if (primaryCategory.Key == "Hardware")
        {
            if (content.Contains("laptop") || content.Contains("computer")) categorization["subcategory"] = "Computer";
            else if (content.Contains("printer")) categorization["subcategory"] = "Printer";
            else if (content.Contains("monitor") || content.Contains("display")) categorization["subcategory"] = "Display";
        }
        
        // Pattern-based insights
        var patternInsights = new JObject();
        
        // Urgency indicators
        var urgencyWords = new[] { "urgent", "asap", "immediately", "critical", "emergency" };
        patternInsights["urgency_indicators"] = urgencyWords.Count(word => content.Contains(word));
        
        // Check for recurring issues
        patternInsights["is_recurring"] = content.Contains("again") || content.Contains("still") || content.Contains("keeps happening");
        
        // Check for multiple issues
        patternInsights["multiple_issues"] = content.Contains("also") || content.Contains("another") || content.Contains("additionally");
        
        // Detect specific patterns
        if (primaryCategory.Key == "Hardware")
        {
            var symptoms = new JArray();
            if (content.Contains("blue screen")) symptoms.Add("BSOD");
            if (content.Contains("not turning on") || content.Contains("won't start")) symptoms.Add("Boot failure");
            if (content.Contains("overheating")) symptoms.Add("Thermal issue");
            patternInsights["physical_symptoms"] = symptoms;
        }
        
        if (primaryCategory.Key == "Network")
        {
            var symptoms = new JArray();
            if (content.Contains("timeout")) symptoms.Add("Connection timeout");
            if (content.Contains("slow")) symptoms.Add("Slow connection");
            if (content.Contains("can't connect") || content.Contains("cannot connect")) symptoms.Add("Connection failure");
            patternInsights["network_symptoms"] = symptoms;
        }
        
        // Error code detection
        var errorPattern = @"(error|code)\s*:?\s*([0-9A-Z]{3,})";
        var errorMatches = Regex.Matches(content, errorPattern, RegexOptions.IgnoreCase);
        if (errorMatches.Count > 0)
        {
            var errorCodes = new JArray();
            foreach (Match match in errorMatches)
            {
                errorCodes.Add(match.Groups[2].Value);
            }
            patternInsights["error_patterns"] = errorCodes;
        }
        
        categorization["pattern_insights"] = patternInsights;
        
        return categorization;
    }
    
    /// <summary>
    /// Get category keywords for relevance calculation
    /// </summary>
    private string[] GetCategoryKeywords(string category)
    {
        switch (category)
        {
            case "Hardware":
                return new[] { "computer", "laptop", "desktop", "monitor", "keyboard", "mouse", "printer", "scanner", "hardware", "device", "equipment", "machine" };
            
            case "Software":
                return new[] { "software", "application", "app", "program", "install", "update", "version", "license", "crash", "error", "bug" };
            
            case "Network":
                return new[] { "network", "internet", "wifi", "wireless", "connection", "vpn", "firewall", "router", "switch", "bandwidth", "lan", "wan" };
            
            case "Access Management":
                return new[] { "access", "permission", "denied", "unauthorized", "login", "account", "credential", "authentication", "group", "role", "rights" };
            
            case "Email/Collaboration":
                return new[] { "email", "outlook", "exchange", "teams", "sharepoint", "onedrive", "calendar", "meeting", "mailbox", "distribution", "list" };
            
            case "Password Reset":
                return new[] { "password", "reset", "forgot", "expired", "locked", "unlock", "change", "forgotten" };
            
            case "Security":
                return new[] { "security", "virus", "malware", "threat", "breach", "attack", "phishing", "spam", "firewall", "antivirus", "suspicious" };
            
            case "Database":
                return new[] { "database", "sql", "query", "table", "backup", "restore", "performance", "index", "stored procedure", "data" };
            
            case "Server/Infrastructure":
                return new[] { "server", "infrastructure", "datacenter", "virtual", "vm", "storage", "backup", "disaster recovery", "cloud", "azure", "aws" };
            
            default:
                return new string[0];
        }
    }
    
    /// <summary>
    /// Translate natural language query to FreshService API filters
    /// </summary>
    private string TranslateNaturalLanguageQuery(string query)
    {
        query = query.ToLower().Trim();
        var filters = new List<string>();
        
        // Time-based queries
        if (query.Contains("today"))
        {
            filters.Add($"created_at:>'{DateTime.UtcNow.Date:yyyy-MM-dd}'" );
        }
        else if (query.Contains("yesterday"))
        {
            var yesterday = DateTime.UtcNow.Date.AddDays(-1);
            filters.Add($"created_at:>'{yesterday:yyyy-MM-dd}' AND created_at:<'{DateTime.UtcNow.Date:yyyy-MM-dd}'" );
        }
        else if (query.Contains("this week"))
        {
            var weekStart = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek);
            filters.Add($"created_at:>'{weekStart:yyyy-MM-dd}'" );
        }
        else if (query.Contains("last week"))
        {
            var weekStart = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek - 7);
            var weekEnd = weekStart.AddDays(7);
            filters.Add($"created_at:>'{weekStart:yyyy-MM-dd}' AND created_at:<'{weekEnd:yyyy-MM-dd}'" );
        }
        
        // Priority-based queries
        if (query.Contains("urgent") || query.Contains("critical"))
        {
            filters.Add("priority:4");
        }
        else if (query.Contains("high priority"))
        {
            filters.Add("priority:3 OR priority:4");
        }
        else if (query.Contains("low priority"))
        {
            filters.Add("priority:1");
        }
        
        // Status-based queries
        if (query.Contains("open"))
        {
            filters.Add("status:2");
        }
        else if (query.Contains("pending"))
        {
            filters.Add("status:3");
        }
        else if (query.Contains("resolved"))
        {
            filters.Add("status:4");
        }
        else if (query.Contains("closed"))
        {
            filters.Add("status:5");
        }
        else if (query.Contains("waiting on customer") || query.Contains("waiting for customer"))
        {
            filters.Add("status:6");
        }
        else if (query.Contains("unresolved"))
        {
            filters.Add("status:2 OR status:3 OR status:6");
        }
        
        // Assignment queries
        if (query.Contains("unassigned"))
        {
            filters.Add("agent_id:null");
        }
        else if (query.Contains("my tickets") || query.Contains("assigned to me"))
        {
            // This would need the current user's ID
            filters.Add("agent_id:me");
        }
        
        // Special conditions
        if (query.Contains("overdue"))
        {
            filters.Add($"due_by:<'{DateTime.UtcNow:yyyy-MM-dd HH:mm}'" );
        }
        
        if (query.Contains("vip"))
        {
            filters.Add("requester.is_vip:true");
        }
        
        // Category queries
        if (query.Contains("network issues") || query.Contains("network problems"))
        {
            filters.Add("(subject:'network' OR description:'network')");
        }
        else if (query.Contains("password reset"))
        {
            filters.Add("(subject:'password reset' OR description:'password reset')");
        }
        else if (query.Contains("hardware"))
        {
            filters.Add("(subject:'hardware' OR description:'hardware' OR subject:'computer' OR subject:'laptop')");
        }
        
        // Combine filters
        return filters.Count > 0 ? string.Join(" AND ", filters) : "";
    }
    
    #endregion
}
