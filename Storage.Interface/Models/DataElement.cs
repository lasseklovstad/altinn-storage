using System;
using System.Collections.Generic;

using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Represents metadata about a data element.
    /// </summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class DataElement : ChangableElement
    {
        /// <summary>
        /// Gets or sets the unique id, a guid.
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the id of the instance which the data element belongs to.
        /// This field is normally not populated if data element is part of instance metadata.
        /// </summary>
        [JsonProperty(PropertyName = "instanceGuid")]
        public string InstanceGuid;

        /// <summary>
        /// Gets or sets the data type, must be equal to the ones defined in application data types.
        /// </summary>
        [JsonProperty(PropertyName = "dataType")]
        public string DataType { get; set; }

        /// <summary>
        /// Gets or sets the name of the data element (file)
        /// </summary>
        [JsonProperty(PropertyName = "filename")]
        public string Filename { get; set; }

        /// <summary>
        /// Gets or sets the content type in the stored data element (file).
        /// </summary>
        [JsonProperty(PropertyName = "contentType")]
        public string ContentType { get; set; }

        /// <summary>
        /// Gets or sets the path to blob storage. Might be nullified in export.
        /// </summary>
        [JsonProperty(PropertyName = "blobStoragePath")]
        public string BlobStoragePath { get; set; }

        /// <summary>
        /// Gets or sets links to access the data element.
        /// </summary>
        [JsonProperty(PropertyName = "selfLinks")]
        public ResourceLinks SelfLinks { get; set; }

        /// <summary>
        /// Gets or sets the size of file in bytes
        /// </summary>
        [JsonProperty(PropertyName = "size")]
        public long Size { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the element can be updated.
        /// </summary>
        [JsonProperty(PropertyName = "locked")]
        public bool Locked { get; set; }

        /// <summary>
        /// Gets or sets information about when the application owner downloaded and confirmed download of the element.
        /// </summary>
        [JsonProperty(PropertyName  = "appOwner")]
        public ApplicationOwnerDataState AppOwner;

        /// <summary>
        /// Gets or sets an optional array of data element references.
        /// </summary>
        [JsonProperty(PropertyName = "refs")]
        public List<Guid> Refs { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    /// <summary>
    /// Represents a container object with a list of data elements.
    /// </summary>
    /// <remarks>
    /// This should be used only when an API endpoint would otherwise return a list of data elements.
    /// Not when the list is a property of a separate class.
    /// </remarks>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class DataElementList
    {
        /// <summary>
        /// The actual list of data elements.
        /// </summary>
        [JsonProperty(PropertyName = "dataElements")]
        public List<DataElement> DataElements { get; set; }
    }
}
