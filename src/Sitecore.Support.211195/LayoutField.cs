using System;
using System.Collections;
using System.Xml.Linq;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Layouts;
using Sitecore.Links;
using Sitecore.Text;

namespace Sitecore.Support.Data.Fields
{
  /// <summary>Represents a Layout field.</summary>
  public class LayoutField : Sitecore.Data.Fields.LayoutField
  {
    /// <summary>Initializes a new instance of the <see cref="LayoutField"/> class. Creates LayoutField from specific item.</summary>
    /// <param name="item">Item to get layout for.</param>
    public LayoutField([NotNull] Item item)
      : base(item)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutField"/> class.
    /// </summary>
    /// <param name="innerField">Inner field.</param>
    public LayoutField([NotNull] Field innerField) : base(innerField)
    {      
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutField"/> class.
    /// </summary>
    /// <param name="innerField">The inner field.</param>
    /// <param name="runtimeValue">The runtime value.</param>
    public LayoutField([NotNull] Field innerField, [NotNull] string runtimeValue)
      : base(innerField, runtimeValue)
    {      
    }

    /// <summary>Removes the link.</summary>
    /// <param name="itemLink">The item link.</param>
    public override void RemoveLink([NotNull] ItemLink itemLink)
    {
      Assert.ArgumentNotNull(itemLink, "itemLink");

      string value = this.Value;
      if (string.IsNullOrEmpty(value))
      {
        return;
      }

      LayoutDefinition layoutDefinition = LayoutDefinition.Parse(value);
      ArrayList devices = layoutDefinition.Devices;
      if (devices == null)
      {
        return;
      }

      string targetItemID = itemLink.TargetItemID.ToString();

      for (int n = devices.Count - 1; n >= 0; n--)
      {
        var device = devices[n] as DeviceDefinition;
        if (device == null)
        {
          continue;
        }

        if (device.ID == targetItemID)
        {
          devices.Remove(device);
          continue;
        }

        if (device.Layout == targetItemID)
        {
          device.Layout = null;
          continue;
        }

        if (device.Placeholders != null)
        {
          string targetPath = itemLink.TargetPath;
          bool isLinkFound = false;
          for (int j = device.Placeholders.Count - 1; j >= 0; j--)
          {
            var placeholderDefinition = device.Placeholders[j] as PlaceholderDefinition;
            if (placeholderDefinition == null)
            {
              continue;
            }

            if (
              string.Equals(
                placeholderDefinition.MetaDataItemId, targetPath, StringComparison.InvariantCultureIgnoreCase) ||
              string.Equals(
                placeholderDefinition.MetaDataItemId, targetItemID, StringComparison.InvariantCultureIgnoreCase))
            {
              device.Placeholders.Remove(placeholderDefinition);
              isLinkFound = true;
            }
          }

          if (isLinkFound)
          {
            continue;
          }
        }

        if (device.Renderings == null)
        {
          continue;
        }

        for (int r = device.Renderings.Count - 1; r >= 0; r--)
        {
          var rendering = device.Renderings[r] as RenderingDefinition;
          if (rendering == null)
          {
            continue;
          }

          if (rendering.Datasource == itemLink.TargetPath)
          {
            rendering.Datasource = string.Empty;
          }

          if (rendering.ItemID == targetItemID)
          {
            device.Renderings.Remove(rendering);
          }

          if (rendering.Datasource == targetItemID)
          {
            rendering.Datasource = string.Empty;
          }

          if (!string.IsNullOrEmpty(rendering.Parameters))
          {
            if (rendering.ItemID == null)
            {
              continue;
            }

            Item layoutItem = this.InnerField.Database.GetItem(rendering.ItemID);

            if (layoutItem == null)
            {
              continue;
            }

            var renderingParametersFieldCollection = this.GetParametersFields(layoutItem, rendering.Parameters);

            foreach (var field in renderingParametersFieldCollection.Values)
            {
              if (!string.IsNullOrEmpty(field.Value))
              {
                field.RemoveLink(itemLink);
              }
            }

            rendering.Parameters = renderingParametersFieldCollection.GetParameters().ToString();
          }

          if (rendering.Rules != null)
          {
            var rulesField = new RulesField(this.InnerField, rendering.Rules.ToString());
            rulesField.RemoveLink(itemLink);
            rendering.Rules = XElement.Parse(rulesField.Value);
          }
        }
      }

      this.Value = layoutDefinition.ToXml();
    }

    /// <summary>
    /// Gets the parameters fields.
    /// </summary>
    /// <param name="layoutItem">The layout item.</param>
    /// <param name="renderingParameters">The rendering parameters.</param>
    /// <returns></returns>
    private RenderingParametersFieldCollection GetParametersFields(Item layoutItem, string renderingParameters)
    {
      var urlParametersString = new UrlString(renderingParameters);
      RenderingParametersFieldCollection parametersFields;

      //layoutItem.Template.CreateItemFrom()

      RenderingParametersFieldCollection.TryParse(layoutItem, urlParametersString, out parametersFields);

      return parametersFields;
    }
  }
}