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
      var linkRemover = new LayoutField.LinkRemover(this);
      linkRemover.RemoveLink(itemLink);
    }

    /// <summary>Relinks the specified item.</summary>
    /// <param name="itemLink">The item link.</param>
    /// <param name="newLink">The new link.</param>
    public override void Relink([NotNull] ItemLink itemLink, [NotNull] Item newLink)
    {
      Assert.ArgumentNotNull(itemLink, "itemLink");
      Assert.ArgumentNotNull(newLink, "newLink");

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
      string newLinkID = newLink.ID.ToString();

      for (int n = devices.Count - 1; n >= 0; n--)
      {
        var device = devices[n] as DeviceDefinition;
        if (device == null)
        {
          continue;
        }

        if (device.ID == targetItemID)
        {
          device.ID = newLinkID;
          continue;
        }

        if (device.Layout == targetItemID)
        {
          device.Layout = newLinkID;
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
              placeholderDefinition.MetaDataItemId = newLink.ID.ToString();
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

          if (rendering.ItemID == targetItemID)
          {
            rendering.ItemID = newLinkID;
          }

          if (rendering.Datasource == targetItemID)
          {
            rendering.Datasource = newLinkID;
          }

          if (rendering.Datasource == itemLink.TargetPath)
          {
            rendering.Datasource = newLink.Paths.FullPath;
          }

          if (!string.IsNullOrEmpty(rendering.Parameters))
          {

            #region fix for Bug 211195

            if (rendering.ItemID == null)
            {
              continue;
            }

            #endregion

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
                field.Relink(itemLink, newLink);
              }
            }

            rendering.Parameters = renderingParametersFieldCollection.GetParameters().ToString();
          }

          if (rendering.Rules != null)
          {
            var rulesField = new RulesField(this.InnerField, rendering.Rules.ToString());
            rulesField.Relink(itemLink, newLink);
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

    private class LinkRemover
    {
      #region Properties

      private readonly LayoutField layout;

      #endregion

      #region C'tors

      public LinkRemover(LayoutField layout)
      {
        this.layout = layout;
      }

      #endregion

      #region Public Methods

      public void RemoveLink([NotNull] ItemLink itemLink)
      {
        Assert.ArgumentNotNull(itemLink, "itemLink");

        string value = this.layout.Value;

        if (string.IsNullOrEmpty(value)) { return; }

        LayoutDefinition layoutDefinition = LayoutDefinition.Parse(value);

        this.DoRemoveLink(itemLink, layoutDefinition);
      }

      #endregion

      #region Private Methods

      private void DoRemoveLink(ItemLink itemLink, LayoutDefinition layoutDefinition)
      {
        this.RemoveLinkFromDevices(itemLink, layoutDefinition.Devices);

        this.SaveLayoutField(layoutDefinition);
      }

      private void RemoveLinkFromDevices(ItemLink itemLink, ArrayList devices)
      {
        if (devices == null) { return; }

        for (int n = devices.Count - 1; n >= 0; n--)
        {
          var device = devices[n] as DeviceDefinition;

          if (device == null) { continue; }

          if (CheckDeviceToRemove(device, itemLink))
          {
            devices.Remove(device);
            continue;
          }

          this.RemoveLinkFromDevice(itemLink, device);
        }
      }

      private void RemoveLinkFromDevice(ItemLink itemLink, DeviceDefinition device)
      {
        if (CheckLayoutToRemove(device.Layout, itemLink))
        {
          device.Layout = null;
          return;
        }

        this.RemoveLinkFromPlaceHolders(itemLink, device.Placeholders);

        this.RemoveLinkFromRenderings(itemLink, device.Renderings);
      }

      private void RemoveLinkFromPlaceHolders(ItemLink itemLink, ArrayList placeholders)
      {
        if (placeholders == null) { return; }

        for (int j = placeholders.Count - 1; j >= 0; j--)
        {
          this.RemoveLinkFromPlaceHolder(itemLink, placeholders, placeholders[j] as PlaceholderDefinition);
        }
      }

      private void RemoveLinkFromPlaceHolder(ItemLink itemLink, ArrayList placeholders, PlaceholderDefinition placeholder)
      {
        if (placeholder == null) { return; }

        if (CheckPlaceHolderToRemove(placeholder, itemLink))
        {
          placeholders.Remove(placeholder);
        }
      }

      private void RemoveLinkFromRenderings(ItemLink itemLink, ArrayList renderings)
      {
        if (renderings == null) { return; }

        for (int r = renderings.Count - 1; r >= 0; r--)
        {
          var rendering = renderings[r] as RenderingDefinition;

          if (rendering == null) { continue; }

          if (CheckRenderingToRemove(rendering, itemLink))
          {
            renderings.Remove(rendering);
            continue;
          }

          this.RemoveLinkFromRendering(itemLink, renderings[r] as RenderingDefinition);
        }
      }

      private void RemoveLinkFromRendering(ItemLink itemLink, RenderingDefinition rendering)
      {
        string targetItemId = itemLink.TargetItemID.ToString();

        if (rendering.Datasource == itemLink.TargetPath)
        {
          rendering.Datasource = string.Empty;
        }

        if (rendering.Datasource == targetItemId)
        {
          rendering.Datasource = string.Empty;
        }

        if (rendering.MultiVariateTest == targetItemId)
        {
          rendering.MultiVariateTest = null;
        }

        this.RemoveLinkFromRenderingParameters(itemLink, rendering);

        this.RemoveLinkFromRenderingRules(itemLink, rendering);
      }

      private void RemoveLinkFromRenderingRules(ItemLink itemLink, RenderingDefinition rendering)
      {
        if (rendering.Rules == null) { return; }

        var rulesField = new RulesField(this.layout.InnerField, rendering.Rules.ToString());
        rulesField.RemoveLink(itemLink);
        rendering.Rules = XElement.Parse(rulesField.Value);
      }

      private void RemoveLinkFromRenderingParameters(ItemLink itemLink, RenderingDefinition rendering)
      {
        if (string.IsNullOrEmpty(rendering.Parameters)) { return; }

        #region fix for Bug 211195
        if (rendering.ItemID == null)
        {
          return;
        }

        #endregion

        var renderingItemId = rendering.ItemID;

        Assert.IsNotNull(renderingItemId, nameof(renderingItemId));

        Item layoutItem = this.layout.InnerField.Database.GetItem(renderingItemId);

        if (layoutItem == null) { return; }

        var renderingParametersFieldCollection = this.layout.GetParametersFields(layoutItem, rendering.Parameters);

        foreach (var field in renderingParametersFieldCollection.Values)
        {
          RemoveLinkFromCustomField(itemLink, field);
        }

        rendering.Parameters = renderingParametersFieldCollection.GetParameters().ToString();
      }

      private void SaveLayoutField(LayoutDefinition layoutDefinition)
      {
        this.layout.Value = layoutDefinition.ToXml();
      }
      #endregion

      #region Static Methods

      private static void RemoveLinkFromCustomField(ItemLink itemLink, CustomField field)
      {
        if (!IsCustomFieldHasLink(field, itemLink)) { return; }

        bool handleEditingContext = !field.InnerField.Item.Editing.IsEditing;

        if (handleEditingContext) { field.InnerField.Item.Editing.BeginEdit(); }

        try
        {
          field.RemoveLink(itemLink);
        }
        finally
        {
          if (handleEditingContext) { field.InnerField.Item.Editing.EndEdit(); }
        }
      }

      private static bool IsCustomFieldHasLink(CustomField field, ItemLink itemLink)
      {
        Assert.IsNotNull(field, nameof(field));
        Assert.IsNotNull(itemLink, nameof(itemLink));

        if (string.IsNullOrEmpty(field.Value)) return false;

        return StringUtil.Contains(field.Value, itemLink.TargetPath, StringComparison.OrdinalIgnoreCase) ||
               StringUtil.Contains(field.Value, itemLink.TargetItemID.ToString(), StringComparison.OrdinalIgnoreCase);
      }

      #endregion

      #region Checkers

      private static bool CheckDeviceToRemove(DeviceDefinition device, ItemLink itemLink)
      {
        return device.ID == itemLink.TargetItemID.ToString();
      }

      private static bool CheckLayoutToRemove(string layoutId, ItemLink itemLink)
      {
        return layoutId == itemLink.TargetItemID.ToString();
      }

      private static bool CheckPlaceHolderToRemove(PlaceholderDefinition placeholder, ItemLink itemLink)
      {
        string targetPath = itemLink.TargetPath;
        string targetItemId = itemLink.TargetItemID.ToString();

        return string.Equals(placeholder.MetaDataItemId, targetPath, StringComparison.InvariantCultureIgnoreCase) ||
               string.Equals(placeholder.MetaDataItemId, targetItemId, StringComparison.InvariantCultureIgnoreCase);
      }

      private static bool CheckRenderingToRemove(RenderingDefinition rendering, ItemLink itemLink)
      {
        return rendering.ItemID == itemLink.TargetItemID.ToString();
      }
      #endregion
    }
  }
}