using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints specifically for the Altinn II message box.
    /// </summary>
    [Route("storage/api/v1/sbl/instances")]
    [ApiController]
    public class MessageBoxInstancesController : ControllerBase
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IInstanceEventRepository _instanceEventRepository;
        private readonly ITextRepository _textRepository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly IAuthorization _authorizationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageBoxInstancesController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="instanceEventRepository">the instance event repository handler</param>
        /// <param name="textRepository">the text repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="authorizationService">the authorization service</param>
        public MessageBoxInstancesController(
            IInstanceRepository instanceRepository,
            IInstanceEventRepository instanceEventRepository,
            ITextRepository textRepository,
            IApplicationRepository applicationRepository,
            IAuthorization authorizationService)
        {
            _instanceRepository = instanceRepository;
            _instanceEventRepository = instanceEventRepository;
            _textRepository = textRepository;
            _applicationRepository = applicationRepository;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Search through instances to find match based on query params.
        /// </summary>
        /// <param name="instanceOwnerPartyId">The instance owner party id</param>
        /// <param name="appId">The application id</param>
        /// <param name="includeActive">Boolean indicating whether to include active instances.</param>
        /// <param name="includeArchived">Boolean indicating whether to include archived instances.</param>
        /// <param name="includeDeleted">Boolean indicating whether to include deleted instances.</param>
        /// <param name="lastChanged">Last changed date.</param>
        /// <param name="created">Created time.</param>
        /// <param name="searchString">Search string.</param>
        /// <param name="archiveReference">The archive reference.</param>
        /// <param name="language">Language nb, en, nn</param>
        /// <returns>list of instances</returns>
        [Obsolete("Replaced with post-endpoint")]
        [Authorize]
        [HttpGet("search")]
        public async Task<ActionResult> SearchMessageBoxInstances(
            [FromQuery(Name = "instanceOwner.partyId")] int instanceOwnerPartyId,
            [FromQuery] string appId,
            [FromQuery] bool includeActive,
            [FromQuery] bool includeArchived,
            [FromQuery] bool includeDeleted,
            [FromQuery] string lastChanged,
            [FromQuery] string created,
            [FromQuery] string searchString,
            [FromQuery] string archiveReference,
            [FromQuery] string language)
        {
            Dictionary<string, StringValues> queryParams = QueryHelpers.ParseQuery(Request.QueryString.Value);

            if (!string.IsNullOrEmpty(archiveReference))
            {
                if ((includeActive == includeArchived) && (includeActive == includeDeleted))
                {
                    includeDeleted = true;
                    includeArchived = true;
                }
                else if (includeActive && !includeArchived && !includeDeleted)
                {
                    return Ok(new List<MessageBoxInstance>());
                }

                includeActive = false;
            }

            GetStatusFromQueryParams(includeActive, includeArchived, includeDeleted, queryParams);
            queryParams.Add("sortBy", "desc:lastChanged");
            queryParams.Add("status.isHardDeleted", "false");

            if (!string.IsNullOrEmpty(searchString))
            {
                StringValues applicationIds = await MatchStringToAppTitle(searchString);
                if (!applicationIds.Any() || (!string.IsNullOrEmpty(appId) && !applicationIds.Contains(appId)))
                {
                    return Ok(new List<MessageBoxInstance>());
                }
                else if (string.IsNullOrEmpty(appId))
                {
                    queryParams.Add("appId", applicationIds);
                }

                queryParams.Remove(nameof(searchString));
            }

            InstanceQueryResponse queryResponse = await _instanceRepository.GetInstancesFromQuery(queryParams, null, 100);

            if (queryResponse?.Exception != null)
            {
                if (queryResponse.Exception.StartsWith("Unknown query parameter"))
                {
                    return BadRequest(queryResponse.Exception);
                }

                return StatusCode(500, queryResponse.Exception);
            }

            return await ProcessQueryResponse(queryResponse, language);
        }

        /// <summary>
        /// Search through instances to find match based on query params.
        /// </summary>
        /// <param name="queryModel">Object with query-params</param>
        /// <returns>List of messagebox instances</returns>
        [Authorize]
        [HttpPost("search")]
        public async Task<ActionResult> SearchMessageBoxInstances([FromBody] MessageBoxQueryModel queryModel)
        {
            if (!string.IsNullOrEmpty(queryModel.ArchiveReference))
            {
                if ((queryModel.IncludeActive == queryModel.IncludeArchived) && (queryModel.IncludeActive == queryModel.IncludeDeleted))
                {
                    queryModel.IncludeDeleted = true;
                    queryModel.IncludeArchived = true;
                }
                else if (queryModel.IncludeActive && !queryModel.IncludeArchived && !queryModel.IncludeDeleted)
                {
                    return Ok(new List<MessageBoxInstance>());
                }

                queryModel.IncludeActive = false;
            }

            Dictionary<string, StringValues> queryParams = GetQueryParams(queryModel);
            GetStatusFromQueryParams(queryModel.IncludeActive, queryModel.IncludeArchived, queryModel.IncludeDeleted, queryParams);
            queryParams.Add("sortBy", "desc:lastChanged");
            queryParams.Add("status.isHardDeleted", "false");

            if (!string.IsNullOrEmpty(queryModel.SearchString))
            {
                StringValues applicationIds = await MatchStringToAppTitle(queryModel.SearchString);
                if (!applicationIds.Any() || (!string.IsNullOrEmpty(queryModel.AppId) && !applicationIds.Contains(queryModel.AppId)))
                {
                    return Ok(new List<MessageBoxInstance>());
                }
                else if (string.IsNullOrEmpty(queryModel.AppId))
                {
                    queryParams.Add("appId", applicationIds);
                }

                queryParams.Remove("searchString");
            }

            InstanceQueryResponse queryResponse = await _instanceRepository.GetInstancesFromQuery(queryParams, null, 100);

            AddQueryModelToTelemetry(queryModel);

            if (queryResponse?.Exception != null)
            {
                return StatusCode(500, queryResponse.Exception);
            }

            return await ProcessQueryResponse(queryResponse, queryModel.Language);
        }

        /// <summary>
        /// Gets all instances in a given state for a given instance owner.
        /// </summary>
        /// <param name="instanceOwnerPartyId">the instance owner id</param>
        /// <param name="instanceGuid">the instance guid</param>
        /// <param name="language"> language id en, nb, nn-NO"</param>
        /// <returns>list of instances</returns>
        [Authorize]
        [HttpGet("{instanceOwnerPartyId:int}/{instanceGuid:guid}")]
        public async Task<ActionResult> GetMessageBoxInstance(
            int instanceOwnerPartyId,
            Guid instanceGuid,
            [FromQuery] string language)
        {
            string[] acceptedLanguages = { "en", "nb", "nn" };
            string languageId = "nb";

            if (language != null && acceptedLanguages.Contains(language.ToLower()))
            {
                languageId = language;
            }

            Instance instance = await _instanceRepository.GetOne(instanceOwnerPartyId, instanceGuid);

            if (instance == null)
            {
                return NotFound($"Could not find instance {instanceOwnerPartyId}/{instanceGuid}");
            }

            List<MessageBoxInstance> authorizedInstanceList =
                await _authorizationService.AuthorizeMesseageBoxInstances(
                    HttpContext.User, new List<Instance> { instance });
            if (authorizedInstanceList.Count <= 0)
            {
                return Forbid();
            }

            MessageBoxInstance authorizedInstance = authorizedInstanceList.First();

            // get app texts and exchange all text keys.
            List<TextResource> texts = await _textRepository.Get(new List<string> { instance.AppId }, languageId);
            InstanceHelper.ReplaceTextKeys(new List<MessageBoxInstance> { authorizedInstance }, texts, languageId);

            return Ok(authorizedInstance);
        }

        /// <summary>
        /// Gets all instances in a given state for a given instance owner.
        /// </summary>
        /// <param name="instanceOwnerPartyId">the instance owner id</param>
        /// <param name="instanceGuid">the instance guid</param>
        /// <returns>list of instances</returns>
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_READ)]
        [HttpGet("{instanceOwnerPartyId:int}/{instanceGuid:guid}/events")]
        public async Task<ActionResult> GetMessageBoxInstanceEvents(
            [FromRoute] int instanceOwnerPartyId,
            [FromRoute] Guid instanceGuid)
        {
            string instanceId = $"{instanceOwnerPartyId}/{instanceGuid}";
            string[] eventTypes =
            {
                InstanceEventType.Created.ToString(),
                InstanceEventType.Deleted.ToString(),
                InstanceEventType.Saved.ToString(),
                InstanceEventType.Submited.ToString(),
                InstanceEventType.Undeleted.ToString(),
                InstanceEventType.SubstatusUpdated.ToString()
            };

            if (string.IsNullOrEmpty(instanceId))
            {
                return BadRequest("Unable to perform query.");
            }

            List<InstanceEvent> allInstanceEvents =
                await _instanceEventRepository.ListInstanceEvents(instanceId, eventTypes, null, null);

            List<InstanceEvent> filteredInstanceEvents = InstanceEventHelper.RemoveDuplicateEvents(allInstanceEvents);

            return Ok(InstanceHelper.ConvertToSBLInstanceEvent(filteredInstanceEvents));
        }

        /// <summary>
        /// Restore a soft deleted instance
        /// </summary>
        /// <param name="instanceOwnerPartyId">instance owner</param>
        /// <param name="instanceGuid">instance id</param>
        /// <returns>True if the instance was restored.</returns>
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_DELETE)]
        [HttpPut("{instanceOwnerPartyId:int}/{instanceGuid:guid}/undelete")]
        public async Task<ActionResult> Undelete(int instanceOwnerPartyId, Guid instanceGuid)
        {
            Instance instance;

            instance = await _instanceRepository.GetOne(instanceOwnerPartyId, instanceGuid);

            if (instance == null)
            {
                return NotFound($"Didn't find the object that should be restored with instanceId={instanceOwnerPartyId}/{instanceGuid}");
            }

            if (instance.Status.IsHardDeleted)
            {
                return NotFound("Instance was permanently deleted and cannot be restored.");
            }
            else if (instance.Status.IsSoftDeleted)
            {
                instance.LastChangedBy = User.GetUserOrOrgId();
                instance.LastChanged = DateTime.UtcNow;
                instance.Status.IsSoftDeleted = false;
                instance.Status.SoftDeleted = null;

                InstanceEvent instanceEvent = new InstanceEvent
                {
                    Created = DateTime.UtcNow,
                    EventType = InstanceEventType.Undeleted.ToString(),
                    InstanceId = instance.Id,
                    InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                    User = new PlatformUser
                    {
                        UserId = User.GetUserIdAsInt(),
                        AuthenticationLevel = User.GetAuthenticationLevel(),
                        OrgId = User.GetOrg(),
                    }
                };

                await _instanceRepository.Update(instance);
                await _instanceEventRepository.InsertInstanceEvent(instanceEvent);
                return Ok(true);
            }

            return Ok(true);
        }

        /// <summary>
        /// Marks an instance for deletion in storage.
        /// </summary>
        /// <param name="instanceGuid">instance id</param>
        /// <param name="instanceOwnerPartyId">instance owner</param>
        /// <param name="hard">if true is marked for hard delete.</param>
        /// <returns>true if instance was successfully deleted</returns>
        /// DELETE /instances/{instanceId}?instanceOwnerPartyId={instanceOwnerPartyId}?hard={bool}
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_DELETE)]
        [HttpDelete("{instanceOwnerPartyId:int}/{instanceGuid:guid}")]
        public async Task<ActionResult> Delete(Guid instanceGuid, int instanceOwnerPartyId, bool hard)
        {
            string instanceId = $"{instanceOwnerPartyId}/{instanceGuid}";

            Instance instance;

            instance = await _instanceRepository.GetOne(instanceOwnerPartyId, instanceGuid);
            if (instance == null)
            {
                return NotFound($"Didn't find the object that should be deleted with instanceId={instanceId}");
            }

            DateTime now = DateTime.UtcNow;

            instance.Status ??= new InstanceStatus();

            if (hard)
            {
                instance.Status.IsHardDeleted = true;
                instance.Status.IsSoftDeleted = true;
                instance.Status.HardDeleted = now;
                instance.Status.SoftDeleted ??= now;
            }
            else
            {
                instance.Status.IsSoftDeleted = true;
                instance.Status.SoftDeleted = now;
            }

            instance.LastChangedBy = User.GetUserOrOrgId();
            instance.LastChanged = now;

            InstanceEvent instanceEvent = new InstanceEvent
            {
                Created = DateTime.UtcNow,
                EventType = InstanceEventType.Deleted.ToString(),
                InstanceId = instance.Id,
                InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                User = new PlatformUser
                {
                    UserId = User.GetUserIdAsInt(),
                    AuthenticationLevel = User.GetAuthenticationLevel(),
                    OrgId = User.GetOrg(),
                },
            };

            await _instanceRepository.Update(instance);
            await _instanceEventRepository.InsertInstanceEvent(instanceEvent);

            return Ok(true);
        }

        private async Task<StringValues> MatchStringToAppTitle(string searchString)
        {
            List<string> appIds = new List<string>();

            Dictionary<string, string> appTitles = await _applicationRepository.GetAllAppTitles();

            foreach (KeyValuePair<string, string> entry in appTitles)
            {
                if (entry.Value.Contains(searchString.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    appIds.Add(entry.Key);
                }
            }

            return new StringValues(appIds.ToArray());
        }

        private static void GetStatusFromQueryParams(
           bool includeActive,
           bool includeArchived,
           bool includeDeleted,
           Dictionary<string, StringValues> queryParams)
        {
            if ((includeActive == includeArchived) && (includeActive == includeDeleted))
            {
                // no filter required
            }
            else if (!includeArchived && !includeDeleted)
            {
                queryParams.Add("status.isArchived", "false");
                queryParams.Add("status.isSoftDeleted", "false");
            }
            else if (!includeActive && !includeDeleted)
            {
                queryParams.Add("status.isArchived", "true");
                queryParams.Add("status.isSoftDeleted", "false");
            }
            else if (!includeActive && !includeArchived)
            {
                queryParams.Add("status.isSoftDeleted", "true");
            }
            else if (includeActive && includeArchived)
            {
                queryParams.Add("status.isSoftDeleted", "false");
            }
            else if (includeArchived)
            {
                queryParams.Add("status.isArchivedOrSoftDeleted", "true");
            }
            else
            {
                queryParams.Add("status.isActiveOrSoftDeleted", "true");
            }

            queryParams.Remove(nameof(includeActive));
            queryParams.Remove(nameof(includeArchived));
            queryParams.Remove(nameof(includeDeleted));
        }

        private async Task RemoveHiddenInstances(List<Instance> instances)
        {
            List<string> appIds = instances.Select(i => i.AppId).Distinct().ToList();
            Dictionary<string, Application> apps = new();

            foreach (string id in appIds)
            {
                apps.Add(id, await _applicationRepository.FindOne(id, id.Split("/")[0]));
            }

            instances.RemoveAll(i => i.VisibleAfter > DateTime.UtcNow);

            InstanceHelper.RemoveHiddenInstances(apps, instances);
        }

        private static Dictionary<string, StringValues> GetQueryParams(MessageBoxQueryModel queryModel)
        {
            string dateTimeFormat = "yyyy-MM-ddTHH:mm:ss";

            Dictionary<string, StringValues> queryParams = new Dictionary<string, StringValues>();

            queryParams.Add("instanceOwner.partyId", queryModel.InstanceOwnerPartyIdList.Select(i => i.ToString()).ToArray());

            if (!string.IsNullOrEmpty(queryModel.AppId))
            {
                queryParams.Add("appId", queryModel.AppId);
            }

            queryParams.Add("includeActive", queryModel.IncludeActive.ToString());
            queryParams.Add("includeArchived", queryModel.IncludeArchived.ToString());
            queryParams.Add("includeDeleted", queryModel.IncludeDeleted.ToString());

            if (queryModel.FromLastChanged != null)
            {
                queryParams.Add("lastChanged", $"gte:{queryModel.FromLastChanged?.ToString(dateTimeFormat)}");
            }

            if (queryModel.ToLastChanged != null)
            {
                if (queryParams.TryGetValue("lastChanged", out StringValues lastChangedValues))
                {
                    StringValues.Concat(lastChangedValues, $"lte:{queryModel.ToLastChanged?.ToString(dateTimeFormat)}");
                }
                else
                {
                    queryParams.Add("lastChanged", $"lte:{queryModel.ToLastChanged?.ToString(dateTimeFormat)}");
                }
            }

            if (queryModel.FromCreated != null)
            {
                queryParams.Add("created", $"gte:{queryModel.FromCreated?.ToString(dateTimeFormat)}");
            }

            if (queryModel.ToCreated != null)
            {
                if (queryParams.TryGetValue("created", out StringValues createdValues))
                {
                    StringValues.Concat(createdValues, $"lte:{queryModel.ToCreated?.ToString(dateTimeFormat)}");
                }
                else
                {
                    queryParams.Add("created", $"lte:{queryModel.ToCreated?.ToString(dateTimeFormat)}");
                }
            }

            if (!string.IsNullOrEmpty(queryModel.SearchString))
            {
                queryParams.Add("searchString", queryModel.SearchString);
            }

            if (!string.IsNullOrEmpty(queryModel.ArchiveReference))
            {
                queryParams.Add("archiveReference", queryModel.ArchiveReference);
            }

            return queryParams;
        }

        private async Task<ActionResult> ProcessQueryResponse(InstanceQueryResponse queryResponse, string language)
        {
            string[] acceptedLanguages = { "en", "nb", "nn" };

            string languageId = "nb";

            if (language != null && acceptedLanguages.Contains(language.ToLower()))
            {
                languageId = language.ToLower();
            }

            if (queryResponse == null || queryResponse.Count <= 0)
            {
                return Ok(new List<MessageBoxInstance>());
            }

            List<Instance> allInstances = queryResponse.Instances;
            await RemoveHiddenInstances(allInstances);

            if (!allInstances.Any())
            {
                return Ok(new List<MessageBoxInstance>());
            }

            allInstances.ForEach(i =>
            {
                if (i.Status.IsArchived || i.Status.IsSoftDeleted)
                {
                    i.DueBefore = null;
                }
            });

            List<MessageBoxInstance> authorizedInstances =
                    await _authorizationService.AuthorizeMesseageBoxInstances(HttpContext.User, allInstances);

            if (!authorizedInstances.Any())
            {
                return Ok(new List<MessageBoxInstance>());
            }

            List<string> appIds = authorizedInstances.Select(i => InstanceHelper.GetAppId(i)).Distinct().ToList();

            List<TextResource> texts = await _textRepository.Get(appIds, languageId);
            InstanceHelper.ReplaceTextKeys(authorizedInstances, texts, languageId);

            return Ok(authorizedInstances);
        }

        private void AddQueryModelToTelemetry(MessageBoxQueryModel queryModel)
        {
            RequestTelemetry requestTelemetry = HttpContext.Features.Get<RequestTelemetry>();

            if (requestTelemetry == null)
            {
                return;
            }

            requestTelemetry.Properties.Add("search.queryModel", JsonSerializer.Serialize(queryModel));
        }
    }
}
