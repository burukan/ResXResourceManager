﻿namespace tomenglertde.ResXManager.View.Visuals
{
    using System;
    using System.ComponentModel.Composition;
    using System.IO;
    using System.Windows;

    using JetBrains.Annotations;

    using tomenglertde.ResXManager.Infrastructure;

    using TomsToolbox.Composition;
    using TomsToolbox.Wpf.Composition;
    using TomsToolbox.Wpf.Composition.Mef;
    using TomsToolbox.Wpf.Converters;

    /// <summary>
    /// Interaction logic for ConfigurationEditorView.xaml
    /// </summary>
    [DataTemplate(typeof(ConfigurationEditorViewModel))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public partial class ConfigurationEditorView
    {
        [NotNull]
        private readonly ITracer _tracer;

        [ImportingConstructor]
        public ConfigurationEditorView([NotNull] IExportProvider exportProvider, [NotNull] ITracer tracer)
        {
            _tracer = tracer;

            try
            {
                this.SetExportProvider(exportProvider);

                InitializeComponent();
            }
            catch (Exception ex)
            {
                exportProvider.TraceXamlLoaderError(ex);
            }
        }

        private void CommandConverter_Error([NotNull] object sender, [NotNull] ErrorEventArgs e)
        {
            var ex = e.GetException();
            if (ex == null)
                return;

            _tracer.TraceError(ex.ToString());

            MessageBox.Show(ex.Message, Properties.Resources.Title);
        }

        private void SortNodesByKeyCommandConverter_Executing([NotNull] object sender, [NotNull] ConfirmedCommandEventArgs e)
        {
            if (MessageBox.Show(Properties.Resources.SortNodesByKey_Confirmation, Properties.Resources.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }
    }
}
