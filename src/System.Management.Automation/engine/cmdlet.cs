// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma warning disable 1634, 1691
#nullable enable

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Management.Automation.Internal;

namespace System.Management.Automation
{
    /// <summary>
    /// Defines members and overrides used by Cmdlets.
    /// All Cmdlets must derive from <see cref="Cmdlet"/>.
    /// </summary>
    /// <remarks>
    /// There are two ways to create a Cmdlet: by deriving from the Cmdlet base class, and by
    /// deriving from the PSCmdlet base class.  The Cmdlet base class is the primary means by
    /// which users create their own Cmdlets.  Extending this class provides support for the most
    /// common functionality, including object output and record processing.
    /// If your Cmdlet requires access to the MSH Runtime (for example, variables in the session state,
    /// access to the host, or information about the current Cmdlet Providers,) then you should instead
    /// derive from the PSCmdlet base class.
    /// In both cases, users should first develop and implement an object model to accomplish their
    /// task, extending the Cmdlet or PSCmdlet classes only as a thin management layer.
    /// </remarks>
    /// <seealso cref="InternalCommand"/>
    public abstract partial class Cmdlet : InternalCommand
    {
        #region public_properties

        /// <summary>
        /// Lists the common parameters that are added by the PowerShell engine to any cmdlet that derives
        /// from PSCmdlet.
        /// </summary>
        public static HashSet<string> CommonParameters
        {
            get
            {
                return s_commonParameters.Value;
            }
        }

        private static readonly Lazy<HashSet<string>> s_commonParameters = new Lazy<HashSet<string>>(
            () =>
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction",
                    "ErrorVariable", "WarningVariable", "OutVariable",
                    "OutBuffer", "PipelineVariable", "InformationVariable" };
            }
        );

        /// <summary>
        /// Lists the common parameters that are added by the PowerShell engine when a cmdlet defines
        /// additional capabilities (SupportsShouldProcess, SupportsTransactions)
        /// </summary>
        public static HashSet<string> OptionalCommonParameters
        {
            get
            {
                return s_optionalCommonParameters.Value;
            }
        }

        private static readonly Lazy<HashSet<string>> s_optionalCommonParameters = new Lazy<HashSet<string>>(
            () =>
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "WhatIf", "Confirm", "UseTransaction" };
            }
        );

        /// <summary>
        /// Is this command stopping?
        /// </summary>
        /// <remarks>
        /// If Stopping is true, many Cmdlet methods will throw
        /// <see cref="PipelineStoppedException"/>.
        ///
        /// In general, if a Cmdlet's override implementation of ProcessRecord etc.
        /// throws <see cref="PipelineStoppedException"/>, the best thing to do is to
        /// shut down the operation and return to the caller.
        /// It is acceptable to not catch <see cref="PipelineStoppedException"/>
        /// and allow the exception to reach ProcessRecord.
        /// </remarks>
        public bool Stopping
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    return IsStopping;
                }
            }
        }

        /// <summary>
        /// The name of the parameter set in effect.
        /// </summary>
        /// <value>the parameter set name</value>
        internal string _ParameterSetName
        {
            get { return _parameterSetName; }
        }

        /// <summary>
        /// Sets the parameter set.
        /// </summary>
        /// <param name="parameterSetName">
        /// The name of the valid parameter set.
        /// </param>
        internal void SetParameterSetName(string parameterSetName)
        {
            _parameterSetName = parameterSetName;
        }

        private string _parameterSetName = string.Empty;

        #region Override Internal

        /// <summary>
        /// When overridden in the derived class, performs initialization
        /// of command execution.
        /// Default implementation in the base class just returns.
        /// </summary>
        /// <exception cref="Exception">
        /// This method is overridden in the implementation of
        /// individual cmdlets, and can throw literally any exception.
        /// </exception>
        internal override void DoBeginProcessing()
        {
            if (CommandRuntime is MshCommandRuntime mshRuntime)
            {
                if (mshRuntime.UseTransaction &&
                   (!Context.TransactionManager.HasTransaction))
                {
                    string error = TransactionStrings.NoTransactionStarted;

                    if (Context.TransactionManager.IsLastTransactionCommitted)
                    {
                        error = TransactionStrings.NoTransactionStartedFromCommit;
                    }
                    else if (Context.TransactionManager.IsLastTransactionRolledBack)
                    {
                        error = TransactionStrings.NoTransactionStartedFromRollback;
                    }

                    throw new InvalidOperationException(error);
                }
            }

            BeginProcessing();
        }

        /// <summary>
        /// When overridden in the derived class, performs execution
        /// of the command.
        /// </summary>
        /// <exception cref="Exception">
        /// This method is overridden in the implementation of
        /// individual cmdlets, and can throw literally any exception.
        /// </exception>
        internal override void DoProcessRecord()
        {
            ProcessRecord();
        }

        /// <summary>
        /// When overridden in the derived class, performs clean-up
        /// after the command execution.
        /// Default implementation in the base class just returns.
        /// </summary>
        /// <exception cref="Exception">
        /// This method is overridden in the implementation of
        /// individual cmdlets, and can throw literally any exception.
        /// </exception>
        internal override void DoEndProcessing()
        {
            EndProcessing();
        }

        /// <summary>
        /// When overridden in the derived class, interrupts currently
        /// running code within the command. It should interrupt BeginProcessing,
        /// ProcessRecord, and EndProcessing.
        /// Default implementation in the base class just returns.
        /// </summary>
        /// <exception cref="Exception">
        /// This method is overridden in the implementation of
        /// individual cmdlets, and can throw literally any exception.
        /// </exception>
        internal override void DoStopProcessing()
        {
            StopProcessing();
        }

        #endregion Override Internal

        #endregion internal_members

        #region ctor

        /// <summary>
        /// Initializes the new instance of Cmdlet class.
        /// </summary>
        /// <remarks>
        /// Only subclasses of <see cref="Cmdlet"/>
        /// can be created.
        /// </remarks>
        protected Cmdlet()
        {
        }

        #endregion ctor

        #region public_methods

        #region Cmdlet virtuals

        /// <summary>
        /// Gets the resource string corresponding to
        /// baseName and resourceId from the current assembly.
        /// You should override this if you require a different behavior.
        /// </summary>
        /// <param name="baseName">The base resource name.</param>
        /// <param name="resourceId">The resource id.</param>
        /// <returns>The resource string corresponding to baseName and resourceId.</returns>
        /// <exception cref="ArgumentException">
        /// Invalid <paramref name="baseName"/> or <paramref name="resourceId"/>, or
        /// string not found in resources
        /// </exception>
        /// <remarks>
        /// This behavior may be used when the Cmdlet specifies
        /// HelpMessageBaseName and HelpMessageResourceId when defining
        /// <see cref="ParameterAttribute"/>,
        /// or when it uses the
        /// <see cref="ErrorDetails"/>
        /// constructor variants which take baseName and resourceId.
        /// </remarks>
        /// <seealso cref="ParameterAttribute"/>
        /// <seealso cref="ErrorDetails"/>
        public virtual string GetResourceString(string baseName, string resourceId)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (string.IsNullOrEmpty(baseName))
                    throw PSTraceSource.NewArgumentNullException("baseName");

                if (string.IsNullOrEmpty(resourceId))
                    throw PSTraceSource.NewArgumentNullException("resourceId");

                ResourceManager manager = ResourceManagerCache.GetResourceManager(GetType().Assembly, baseName);
                string? retValue = null;

                try
                {
                    retValue = manager.GetString(resourceId, CultureInfo.CurrentUICulture);
                }
                catch (MissingManifestResourceException)
                {
                    throw PSTraceSource.NewArgumentException("baseName", GetErrorText.ResourceBaseNameFailure, baseName);
                }

                if (retValue == null)
                {
                    throw PSTraceSource.NewArgumentException("resourceId", GetErrorText.ResourceIdFailure, resourceId);
                }

                return retValue;
            }
        }

        #endregion Cmdlet virtuals

        #region Write

        /// <summary>
        /// Holds the command runtime object for this command. This object controls
        /// what actually happens when a write is called.
        /// </summary>
        public ICommandRuntime CommandRuntime
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    return commandRuntime;
                }
            }

            set
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    commandRuntime = value;
                }
            }
        }

        /// <summary>
        /// Internal variant: Writes the specified error to the error pipe.
        /// </summary>
        /// <remarks>
        /// Do not call WriteError(e.ErrorRecord).
        /// The ErrorRecord contained in the ErrorRecord property of
        /// an exception which implements IContainsErrorRecord
        /// should not be passed directly to WriteError, since it contains
        /// a <see cref="ParentContainsErrorRecordException"/>
        /// rather than the real exception.
        /// </remarks>
        /// <param name="errorRecord">Error.</param>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread
        /// </exception>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <remarks>
        /// <see cref="ThrowTerminatingError"/>
        /// terminates the command, where
        /// <see cref="ICommandRuntime.WriteError"/>
        /// allows the command to continue.
        ///
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire, this method will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        public void WriteError(ErrorRecord errorRecord)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    commandRuntime.WriteError(errorRecord);
                else
                    throw new NotImplementedException("WriteError");
            }
        }
        /// <summary>
        /// Writes the object to the output pipe.
        /// </summary>
        /// <param name="sendToPipeline">
        /// The object that needs to be written.  This will be written as
        /// a single object, even if it is an enumeration.
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteObject may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <seealso cref="ICommandRuntime.WriteObject(object,bool)"/>
        /// <seealso cref="ICommandRuntime.WriteError(ErrorRecord)"/>
        public void WriteObject(object sendToPipeline)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    commandRuntime.WriteObject(sendToPipeline);
                else
                    throw new NotImplementedException("WriteObject");
            }
        }
        /// <summary>
        /// Writes one or more objects to the output pipe.
        /// If the object is a collection and the enumerateCollection flag
        /// is true, the objects in the collection
        /// will be written individually.
        /// </summary>
        /// <param name="sendToPipeline">
        /// The object that needs to be written to the pipeline.
        /// </param>
        /// <param name="enumerateCollection">
        /// true if the collection should be enumerated
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteObject may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <seealso cref="ICommandRuntime.WriteObject(object)"/>
        /// <seealso cref="ICommandRuntime.WriteError(ErrorRecord)"/>
        public void WriteObject(object sendToPipeline, bool enumerateCollection)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    commandRuntime.WriteObject(sendToPipeline, enumerateCollection);
                else
                    throw new NotImplementedException("WriteObject");
            }
        }

        /// <summary>
        /// Display verbose information.
        /// </summary>
        /// <param name="text">Verbose output.</param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteVerbose may only be called during a call to this Cmdlets's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <remarks>
        /// Use WriteVerbose to display more detailed information about
        /// the activity of your Cmdlet.  By default, verbose output will
        /// not be displayed, although this can be configured with the
        /// VerbosePreference shell variable
        /// or the -Verbose and -Debug command-line options.
        /// </remarks>
        /// <seealso cref="WriteDebug(string)"/>
        /// <seealso cref="WriteWarning(string)"/>
        /// <seealso cref="WriteProgress(ProgressRecord)"/>
        public void WriteVerbose(string text)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    commandRuntime.WriteVerbose(text);
                else
                    throw new NotImplementedException("WriteVerbose");
            }
        }

        /// <summary>
        /// Display warning information.
        /// </summary>
        /// <param name="text">Warning output.</param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteWarning may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <remarks>
        /// Use WriteWarning to display warnings about
        /// the activity of your Cmdlet.  By default, warning output will
        /// be displayed, although this can be configured with the
        /// WarningPreference shell variable
        /// or the -Verbose and -Debug command-line options.
        /// </remarks>
        /// <seealso cref="WriteDebug(string)"/>
        /// <seealso cref="WriteVerbose(string)"/>
        /// <seealso cref="WriteProgress(ProgressRecord)"/>
        public void WriteWarning(string text)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    commandRuntime.WriteWarning(text);
                else
                    throw new NotImplementedException("WriteWarning");
            }
        }

        /// <summary>
        /// Write text into pipeline execution log.
        /// </summary>
        /// <param name="text">Text to be written to log.</param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteWarning may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <remarks>
        /// Use WriteCommandDetail to write important information about cmdlet execution to
        /// pipeline execution log.
        ///
        /// If LogPipelineExecutionDetail is turned on, this information will be written
        /// to monad log under log category "Pipeline execution detail"
        /// </remarks>
        /// <seealso cref="WriteDebug(string)"/>
        /// <seealso cref="WriteVerbose(string)"/>
        /// <seealso cref="WriteProgress(ProgressRecord)"/>
        public void WriteCommandDetail(string text)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    commandRuntime.WriteCommandDetail(text);
                else
                    throw new NotImplementedException("WriteCommandDetail");
            }
        }

        /// <summary>
        /// Display progress information.
        /// </summary>
        /// <param name="progressRecord">Progress information.</param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteProgress may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <remarks>
        /// Use WriteProgress to display progress information about
        /// the activity of your Cmdlet, when the operation of your Cmdlet
        /// could potentially take a long time.
        ///
        /// By default, progress output will
        /// be displayed, although this can be configured with the
        /// ProgressPreference shell variable.
        /// </remarks>
        /// <seealso cref="WriteDebug(string)"/>
        /// <seealso cref="WriteWarning(string)"/>
        /// <seealso cref="WriteVerbose(string)"/>
        public void WriteProgress(ProgressRecord progressRecord)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    commandRuntime.WriteProgress(progressRecord);
                else
                    throw new NotImplementedException("WriteProgress");
            }
        }

        /// <summary>
        /// Displays progress output if enabled.
        /// </summary>
        /// <param name="sourceId">
        /// Identifies which command is reporting progress
        /// </param>
        /// <param name="progressRecord">
        /// Progress status to be displayed
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <remarks>
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire, this method will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        internal void WriteProgress(
            long sourceId,
            ProgressRecord progressRecord)
        {
            if (commandRuntime != null)
                commandRuntime.WriteProgress(sourceId, progressRecord);
            else
                throw new NotImplementedException("WriteProgress");
        }

        /// <summary>
        /// Display debug information.
        /// </summary>
        /// <param name="text">Debug output.</param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteDebug may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <remarks>
        /// Use WriteDebug to display debug information on the inner workings
        /// of your Cmdlet.  By default, debug output will
        /// not be displayed, although this can be configured with the
        /// DebugPreference shell variable or the -Debug command-line option.
        /// </remarks>
        /// <remarks>
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire, this method will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        /// <seealso cref="WriteVerbose(string)"/>
        /// <seealso cref="WriteWarning(string)"/>
        /// <seealso cref="WriteProgress(ProgressRecord)"/>
        public void WriteDebug(string text)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    commandRuntime.WriteDebug(text);
                else
                    throw new NotImplementedException("WriteDebug");
            }
        }

        /// <summary>
        /// Route information to the user or host.
        /// </summary>
        /// <param name="messageData">The object / message data to transmit to the hosting application.</param>
        /// <param name="tags">
        /// Any tags to be associated with the message data. These can later be used to filter
        /// or separate objects being sent to the host.
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteInformation may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <remarks>
        /// Use WriteInformation to transmit information to the user about the activity
        /// of your Cmdlet.  By default, informational output will
        /// be displayed, although this can be configured with the
        /// InformationPreference shell variable or the -InformationPreference command-line option.
        /// </remarks>
        /// <remarks>
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire, this method will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        public void WriteInformation(object messageData, string[] tags)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime is ICommandRuntime2 commandRuntime2)
                {
                    string source = MyInvocation.PSCommandPath;
                    if (string.IsNullOrEmpty(source))
                    {
                        source = MyInvocation.MyCommand.Name;
                    }

                    InformationRecord informationRecord = new InformationRecord(messageData, source);

                    if (tags != null)
                    {
                        informationRecord.Tags.AddRange(tags);
                    }

                    commandRuntime2.WriteInformation(informationRecord);
                }
                else
                {
                    throw new NotImplementedException("WriteInformation");
                }
            }
        }

        /// <summary>
        /// Route information to the user or host.
        /// </summary>
        /// <param name="informationRecord">The information record to write.</param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// WriteInformation may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <remarks>
        /// Use WriteInformation to transmit information to the user about the activity
        /// of your Cmdlet.  By default, informational output will
        /// be displayed, although this can be configured with the
        /// InformationPreference shell variable or the -InformationPreference command-line option.
        /// </remarks>
        /// <remarks>
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire, this method will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        public void WriteInformation(InformationRecord informationRecord)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime is ICommandRuntime2 commandRuntime2)
                {
                    commandRuntime2.WriteInformation(informationRecord);
                }
                else
                {
                    throw new NotImplementedException("WriteInformation");
                }
            }
        }

        #endregion Write

        #region ShouldProcess
        /// <summary>
        /// Confirm the operation with the user.  Cmdlets which make changes
        /// (e.g. delete files, stop services etc.) should call ShouldProcess
        /// to give the user the opportunity to confirm that the operation
        /// should actually be performed.
        /// </summary>
        /// <param name="target">
        /// Name of the target resource being acted upon. This will
        /// potentially be displayed to the user.
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// ShouldProcess may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <returns>
        /// If ShouldProcess returns true, the operation should be performed.
        /// If ShouldProcess returns false, the operation should not be
        /// performed, and the Cmdlet should move on to the next target resource.
        /// </returns>
        /// <remarks>
        /// A Cmdlet should declare
        /// [Cmdlet( SupportsShouldProcess = true )]
        /// if-and-only-if it calls ShouldProcess before making changes.
        ///
        /// ShouldProcess may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        ///
        /// ShouldProcess will take into account command-line settings
        /// and preference variables in determining what it should return
        /// and whether it should prompt the user.
        /// </remarks>
        /// <remarks>
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire,
        /// <see cref="Cmdlet.ShouldProcess(string)"/>
        /// will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        /// <example>
        ///     <snippet Code="C#">
        ///         namespace Microsoft.Samples.MSH.Cmdlet
        ///         {
        ///             [Cmdlet(VerbsCommon.Remove,"myobjecttype1")]
        ///             public class RemoveMyObjectType1 : Cmdlet
        ///             {
        ///                 [Parameter( Mandatory = true )]
        ///                 public string Filename
        ///                 {
        ///                     get { return filename; }
        ///                     set { filename = value; }
        ///                 }
        ///                 private string filename;
        ///
        ///                 public override void ProcessRecord()
        ///                 {
        ///                     if (ShouldProcess(filename))
        ///                     {
        ///                         // delete the object
        ///                     }
        ///                 }
        ///             }
        ///         }
        ///     </snippet>
        /// </example>
        /// <seealso cref="ShouldProcess(string,string)"/>
        /// <seealso cref="ShouldProcess(string,string,string)"/>
        /// <seealso cref="ShouldProcess(string,string,string,out ShouldProcessReason)"/>
        /// <seealso cref="ShouldContinue(string,string)"/>
        /// <seealso cref="ShouldContinue(string,string,ref bool,ref bool)"/>
        public bool ShouldProcess(string target)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    return commandRuntime.ShouldProcess(target);
                else
                    return true;
            }
        }

        /// <summary>
        /// Confirm the operation with the user.  Cmdlets which make changes
        /// (e.g. delete files, stop services etc.) should call ShouldProcess
        /// to give the user the opportunity to confirm that the operation
        /// should actually be performed.
        ///
        /// This variant allows the caller to specify text for both the
        /// target resource and the action.
        /// </summary>
        /// <param name="target">
        /// Name of the target resource being acted upon. This will
        /// potentially be displayed to the user.
        /// </param>
        /// <param name="action">
        /// Name of the action which is being performed. This will
        /// potentially be displayed to the user. (default is Cmdlet name)
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// ShouldProcess may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <returns>
        /// If ShouldProcess returns true, the operation should be performed.
        /// If ShouldProcess returns false, the operation should not be
        /// performed, and the Cmdlet should move on to the next target resource.
        /// </returns>
        /// <remarks>
        /// A Cmdlet should declare
        /// [Cmdlet( SupportsShouldProcess = true )]
        /// if-and-only-if it calls ShouldProcess before making changes.
        ///
        /// ShouldProcess may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        ///
        /// ShouldProcess will take into account command-line settings
        /// and preference variables in determining what it should return
        /// and whether it should prompt the user.
        /// </remarks>
        /// <remarks>
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire, this method will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        /// <example>
        ///     <snippet Code="C#">
        ///         namespace Microsoft.Samples.MSH.Cmdlet
        ///         {
        ///             [Cmdlet(VerbsCommon.Remove,"myobjecttype2")]
        ///             public class RemoveMyObjectType2 : Cmdlet
        ///             {
        ///                 [Parameter( Mandatory = true )]
        ///                 public string Filename
        ///                 {
        ///                     get { return filename; }
        ///                     set { filename = value; }
        ///                 }
        ///                 private string filename;
        ///
        ///                 public override void ProcessRecord()
        ///                 {
        ///                     if (ShouldProcess(filename, "delete"))
        ///                     {
        ///                         // delete the object
        ///                     }
        ///                 }
        ///             }
        ///         }
        ///     </snippet>
        /// </example>
        /// <seealso cref="ShouldProcess(string)"/>
        /// <seealso cref="ShouldProcess(string,string,string)"/>
        /// <seealso cref="ShouldProcess(string,string,string,out ShouldProcessReason)"/>
        /// <seealso cref="ShouldContinue(string,string)"/>
        /// <seealso cref="ShouldContinue(string,string,ref bool,ref bool)"/>
        public bool ShouldProcess(string target, string action)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    return commandRuntime.ShouldProcess(target, action);
                else
                    return true;
            }
        }

        /// <summary>
        /// Confirm the operation with the user.  Cmdlets which make changes
        /// (e.g. delete files, stop services etc.) should call ShouldProcess
        /// to give the user the opportunity to confirm that the operation
        /// should actually be performed.
        ///
        /// This variant allows the caller to specify the complete text
        /// describing the operation, rather than just the name and action.
        /// </summary>
        /// <param name="verboseDescription">
        /// Textual description of the action to be performed.
        /// This is what will be displayed to the user for
        /// ActionPreference.Continue.
        /// </param>
        /// <param name="verboseWarning">
        /// Textual query of whether the action should be performed,
        /// usually in the form of a question.
        /// This is what will be displayed to the user for
        /// ActionPreference.Inquire.
        /// </param>
        /// <param name="caption">
        /// Caption of the window which may be displayed
        /// if the user is prompted whether or not to perform the action.
        /// <paramref name="caption"/> may be displayed by some hosts, but not all.
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// ShouldProcess may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <returns>
        /// If ShouldProcess returns true, the operation should be performed.
        /// If ShouldProcess returns false, the operation should not be
        /// performed, and the Cmdlet should move on to the next target resource.
        /// </returns>
        /// <remarks>
        /// A Cmdlet should declare
        /// [Cmdlet( SupportsShouldProcess = true )]
        /// if-and-only-if it calls ShouldProcess before making changes.
        ///
        /// ShouldProcess may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        ///
        /// ShouldProcess will take into account command-line settings
        /// and preference variables in determining what it should return
        /// and whether it should prompt the user.
        /// </remarks>
        /// <remarks>
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire, this method will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        /// <example>
        ///     <snippet Code="C#">
        ///         namespace Microsoft.Samples.MSH.Cmdlet
        ///         {
        ///             [Cmdlet(VerbsCommon.Remove,"myobjecttype3")]
        ///             public class RemoveMyObjectType3 : Cmdlet
        ///             {
        ///                 [Parameter( Mandatory = true )]
        ///                 public string Filename
        ///                 {
        ///                     get { return filename; }
        ///                     set { filename = value; }
        ///                 }
        ///                 private string filename;
        ///
        ///                 public override void ProcessRecord()
        ///                 {
        ///                     if (ShouldProcess(
        ///                         string.Format("Deleting file {0}",filename),
        ///                         string.Format("Are you sure you want to delete file {0}?", filename),
        ///                         "Delete file"))
        ///                     {
        ///                         // delete the object
        ///                     }
        ///                 }
        ///             }
        ///         }
        ///     </snippet>
        /// </example>
        /// <seealso cref="ShouldProcess(string)"/>
        /// <seealso cref="ShouldProcess(string,string)"/>
        /// <seealso cref="ShouldProcess(string,string,string,out ShouldProcessReason)"/>
        /// <seealso cref="ShouldContinue(string,string)"/>
        /// <seealso cref="ShouldContinue(string,string,ref bool,ref bool)"/>
        public bool ShouldProcess(
            string verboseDescription,
            string verboseWarning,
            string caption)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    return commandRuntime.ShouldProcess(verboseDescription, verboseWarning, caption);
                else
                    return true;
            }
        }

        /// <summary>
        /// Confirm the operation with the user.  Cmdlets which make changes
        /// (e.g. delete files, stop services etc.) should call ShouldProcess
        /// to give the user the opportunity to confirm that the operation
        /// should actually be performed.
        ///
        /// This variant allows the caller to specify the complete text
        /// describing the operation, rather than just the name and action.
        /// </summary>
        /// <param name="verboseDescription">
        /// Textual description of the action to be performed.
        /// This is what will be displayed to the user for
        /// ActionPreference.Continue.
        /// </param>
        /// <param name="verboseWarning">
        /// Textual query of whether the action should be performed,
        /// usually in the form of a question.
        /// This is what will be displayed to the user for
        /// ActionPreference.Inquire.
        /// </param>
        /// <param name="caption">
        /// Caption of the window which may be displayed
        /// if the user is prompted whether or not to perform the action.
        /// <paramref name="caption"/> may be displayed by some hosts, but not all.
        /// </param>
        /// <param name="shouldProcessReason">
        /// Indicates the reason(s) why ShouldProcess returned what it returned.
        /// Only the reasons enumerated in
        /// <see cref="ShouldProcessReason"/>
        /// are returned.
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// ShouldProcess may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <returns>
        /// If ShouldProcess returns true, the operation should be performed.
        /// If ShouldProcess returns false, the operation should not be
        /// performed, and the Cmdlet should move on to the next target resource.
        /// </returns>
        /// <remarks>
        /// A Cmdlet should declare
        /// [Cmdlet( SupportsShouldProcess = true )]
        /// if-and-only-if it calls ShouldProcess before making changes.
        ///
        /// ShouldProcess may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        ///
        /// ShouldProcess will take into account command-line settings
        /// and preference variables in determining what it should return
        /// and whether it should prompt the user.
        /// </remarks>
        /// <remarks>
        /// If the pipeline is terminated due to ActionPreference.Stop
        /// or ActionPreference.Inquire, this method will throw
        /// <see cref="PipelineStoppedException"/>,
        /// but the command failure will ultimately be
        /// <see cref="ActionPreferenceStopException"/>,
        /// </remarks>
        /// <example>
        ///     <snippet Code="C#">
        ///         namespace Microsoft.Samples.MSH.Cmdlet
        ///         {
        ///             [Cmdlet(VerbsCommon.Remove,"myobjecttype3")]
        ///             public class RemoveMyObjectType3 : Cmdlet
        ///             {
        ///                 [Parameter( Mandatory = true )]
        ///                 public string Filename
        ///                 {
        ///                     get { return filename; }
        ///                     set { filename = value; }
        ///                 }
        ///                 private string filename;
        ///
        ///                 public override void ProcessRecord()
        ///                 {
        ///                     ShouldProcessReason shouldProcessReason;
        ///                     if (ShouldProcess(
        ///                         string.Format("Deleting file {0}",filename),
        ///                         string.Format("Are you sure you want to delete file {0}?", filename),
        ///                         "Delete file",
        ///                         out shouldProcessReason))
        ///                     {
        ///                         // delete the object
        ///                     }
        ///                 }
        ///             }
        ///         }
        ///     </snippet>
        /// </example>
        /// <seealso cref="ShouldProcess(string)"/>
        /// <seealso cref="ShouldProcess(string,string)"/>
        /// <seealso cref="ShouldProcess(string,string,string)"/>
        /// <seealso cref="ShouldContinue(string,string)"/>
        /// <seealso cref="ShouldContinue(string,string,ref bool,ref bool)"/>
        public bool ShouldProcess(
            string verboseDescription,
            string verboseWarning,
            string caption,
            out ShouldProcessReason shouldProcessReason)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    return commandRuntime.ShouldProcess(verboseDescription, verboseWarning, caption, out shouldProcessReason);
                else
                {
                    shouldProcessReason = ShouldProcessReason.None;
                    return true;
                }
            }
        }

        #endregion ShouldProcess

        #region ShouldContinue
        /// <summary>
        /// Confirm an operation or grouping of operations with the user.
        /// This differs from ShouldProcess in that it is not affected by
        /// preference settings or command-line parameters,
        /// it always does the query.
        /// This variant only offers Yes/No, not YesToAll/NoToAll.
        /// </summary>
        /// <param name="query">
        /// Textual query of whether the action should be performed,
        /// usually in the form of a question.
        /// </param>
        /// <param name="caption">
        /// Caption of the window which may be displayed
        /// when the user is prompted whether or not to perform the action.
        /// It may be displayed by some hosts, but not all.
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// ShouldContinue may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <returns>
        /// If ShouldContinue returns true, the operation should be performed.
        /// If ShouldContinue returns false, the operation should not be
        /// performed, and the Cmdlet should move on to the next target resource.
        /// </returns>
        /// <remarks>
        /// Cmdlets using ShouldContinue should also offer a "bool Force"
        /// parameter which bypasses the calls to ShouldContinue
        /// and ShouldProcess.
        /// If this is not done, it will be difficult to use the Cmdlet
        /// from scripts and non-interactive hosts.
        ///
        /// Cmdlets using ShouldContinue must still verify operations
        /// which will make changes using ShouldProcess.
        /// This will assure that settings such as -WhatIf work properly.
        /// You may call ShouldContinue either before or after ShouldProcess.
        ///
        /// ShouldContinue may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        ///
        /// Cmdlets may have different "classes" of confirmations.  For example,
        /// "del" confirms whether files in a particular directory should be
        /// deleted, whether read-only files should be deleted, etc.
        /// Cmdlets can use ShouldContinue to store YesToAll/NoToAll members
        /// for each such "class" to keep track of whether the user has
        /// confirmed "delete all read-only files" etc.
        /// ShouldProcess offers YesToAll/NoToAll automatically,
        /// but answering YesToAll or NoToAll applies to all subsequent calls
        /// to ShouldProcess for the Cmdlet instance.
        /// </remarks>
        /// <example>
        ///     <snippet Code="C#">
        ///         namespace Microsoft.Samples.MSH.Cmdlet
        ///         {
        ///             [Cmdlet(VerbsCommon.Remove,"myobjecttype4")]
        ///             public class RemoveMyObjectType4 : Cmdlet
        ///             {
        ///                 [Parameter( Mandatory = true )]
        ///                 public string Filename
        ///                 {
        ///                     get { return filename; }
        ///                     set { filename = value; }
        ///                 }
        ///                 private string filename;
        ///
        ///                 [Parameter]
        ///                 public SwitchParameter Force
        ///                 {
        ///                     get { return force; }
        ///                     set { force = value; }
        ///                 }
        ///                 private bool force;
        ///
        ///                 public override void ProcessRecord()
        ///                 {
        ///                     if (ShouldProcess(
        ///                         string.Format("Deleting file {0}",filename),
        ///                         string.Format("Are you sure you want to delete file {0}", filename),
        ///                         "Delete file"))
        ///                     {
        ///                         if (IsReadOnly(filename))
        ///                         {
        ///                             if (!Force &amp;&amp; !ShouldContinue(
        ///                                     string.Format("File {0} is read-only.  Are you sure you want to delete read-only file {0}?", filename),
        ///                                     "Delete file"))
        ///                                     )
        ///                             {
        ///                                 return;
        ///                             }
        ///                         }
        ///                         // delete the object
        ///                     }
        ///                 }
        ///             }
        ///         }
        ///     </snippet>
        /// </example>
        /// <seealso cref="ShouldContinue(string,string,ref bool,ref bool)"/>
        /// <seealso cref="ShouldProcess(string)"/>
        /// <seealso cref="ShouldProcess(string,string)"/>
        /// <seealso cref="ShouldProcess(string,string,string)"/>
        public bool ShouldContinue(string query, string caption)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    return commandRuntime.ShouldContinue(query, caption);
                else
                    return true;
            }
        }

        /// <summary>
        /// Confirm an operation or grouping of operations with the user.
        /// This differs from ShouldProcess in that it is not affected by
        /// preference settings or command-line parameters,
        /// it always does the query.
        /// This variant offers Yes, No, YesToAll and NoToAll.
        /// </summary>
        /// <param name="query">
        /// Textual query of whether the action should be performed,
        /// usually in the form of a question.
        /// </param>
        /// <param name="caption">
        /// Caption of the window which may be displayed
        /// when the user is prompted whether or not to perform the action.
        /// It may be displayed by some hosts, but not all.
        /// </param>
        /// <param name="yesToAll">
        /// true iff user selects YesToAll.  If this is already true,
        /// ShouldContinue will bypass the prompt and return true.
        /// </param>
        /// <param name="noToAll">
        /// true iff user selects NoToAll.  If this is already true,
        /// ShouldContinue will bypass the prompt and return false.
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// ShouldContinue may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <returns>
        /// If ShouldContinue returns true, the operation should be performed.
        /// If ShouldContinue returns false, the operation should not be
        /// performed, and the Cmdlet should move on to the next target resource.
        /// </returns>
        /// <remarks>
        /// Cmdlets using ShouldContinue should also offer a "bool Force"
        /// parameter which bypasses the calls to ShouldContinue
        /// and ShouldProcess.
        /// If this is not done, it will be difficult to use the Cmdlet
        /// from scripts and non-interactive hosts.
        ///
        /// Cmdlets using ShouldContinue must still verify operations
        /// which will make changes using ShouldProcess.
        /// This will assure that settings such as -WhatIf work properly.
        /// You may call ShouldContinue either before or after ShouldProcess.
        ///
        /// ShouldContinue may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        ///
        /// Cmdlets may have different "classes" of confirmations.  For example,
        /// "del" confirms whether files in a particular directory should be
        /// deleted, whether read-only files should be deleted, etc.
        /// Cmdlets can use ShouldContinue to store YesToAll/NoToAll members
        /// for each such "class" to keep track of whether the user has
        /// confirmed "delete all read-only files" etc.
        /// ShouldProcess offers YesToAll/NoToAll automatically,
        /// but answering YesToAll or NoToAll applies to all subsequent calls
        /// to ShouldProcess for the Cmdlet instance.
        /// </remarks>
        /// <example>
        ///     <snippet Code="C#">
        ///         namespace Microsoft.Samples.MSH.Cmdlet
        ///         {
        ///             [Cmdlet(VerbsCommon.Remove,"myobjecttype4")]
        ///             public class RemoveMyObjectType5 : Cmdlet
        ///             {
        ///                 [Parameter( Mandatory = true )]
        ///                 public string Filename
        ///                 {
        ///                     get { return filename; }
        ///                     set { filename = value; }
        ///                 }
        ///                 private string filename;
        ///
        ///                 [Parameter]
        ///                 public SwitchParameter Force
        ///                 {
        ///                     get { return force; }
        ///                     set { force = value; }
        ///                 }
        ///                 private bool force;
        ///
        ///                 private bool yesToAll;
        ///                 private bool noToAll;
        ///
        ///                 public override void ProcessRecord()
        ///                 {
        ///                     if (ShouldProcess(
        ///                         string.Format("Deleting file {0}",filename),
        ///                         string.Format("Are you sure you want to delete file {0}", filename),
        ///                         "Delete file"))
        ///                     {
        ///                         if (IsReadOnly(filename))
        ///                         {
        ///                             if (!Force &amp;&amp; !ShouldContinue(
        ///                                     string.Format("File {0} is read-only.  Are you sure you want to delete read-only file {0}?", filename),
        ///                                     "Delete file"),
        ///                                     ref yesToAll,
        ///                                     ref noToAll
        ///                                     )
        ///                             {
        ///                                 return;
        ///                             }
        ///                         }
        ///                         // delete the object
        ///                     }
        ///                 }
        ///             }
        ///         }
        ///     </snippet>
        /// </example>
        /// <seealso cref="ShouldContinue(string,string)"/>
        /// <seealso cref="ShouldProcess(string)"/>
        /// <seealso cref="ShouldProcess(string,string)"/>
        /// <seealso cref="ShouldProcess(string,string,string)"/>
        [SuppressMessage("Microsoft.Design", "CA1045:DoNotPassTypesByReference")]
        public bool ShouldContinue(
            string query, string caption, ref bool yesToAll, ref bool noToAll)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    return commandRuntime.ShouldContinue(query, caption, ref yesToAll, ref noToAll);
                else
                    return true;
            }
        }

        /// <summary>
        /// Confirm an operation or grouping of operations with the user.
        /// This differs from ShouldProcess in that it is not affected by
        /// preference settings or command-line parameters,
        /// it always does the query.
        /// This variant offers Yes, No, YesToAll and NoToAll.
        /// </summary>
        /// <param name="query">
        /// Textual query of whether the action should be performed,
        /// usually in the form of a question.
        /// </param>
        /// <param name="caption">
        /// Caption of the window which may be displayed
        /// when the user is prompted whether or not to perform the action.
        /// It may be displayed by some hosts, but not all.
        /// </param>
        /// <param name="hasSecurityImpact">
        /// true if the operation being confirmed has a security impact. If specified,
        /// the default option selected in the selection menu is 'No'.
        /// </param>
        /// <param name="yesToAll">
        /// true iff user selects YesToAll.  If this is already true,
        /// ShouldContinue will bypass the prompt and return true.
        /// </param>
        /// <param name="noToAll">
        /// true iff user selects NoToAll.  If this is already true,
        /// ShouldContinue will bypass the prompt and return false.
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// The pipeline has already been terminated, or was terminated
        /// during the execution of this method.
        /// The Cmdlet should generally just allow PipelineStoppedException
        /// to percolate up to the caller of ProcessRecord etc.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Not permitted at this time or from this thread.
        /// ShouldContinue may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        /// </exception>
        /// <returns>
        /// If ShouldContinue returns true, the operation should be performed.
        /// If ShouldContinue returns false, the operation should not be
        /// performed, and the Cmdlet should move on to the next target resource.
        /// </returns>
        /// <remarks>
        /// Cmdlets using ShouldContinue should also offer a "bool Force"
        /// parameter which bypasses the calls to ShouldContinue
        /// and ShouldProcess.
        /// If this is not done, it will be difficult to use the Cmdlet
        /// from scripts and non-interactive hosts.
        ///
        /// Cmdlets using ShouldContinue must still verify operations
        /// which will make changes using ShouldProcess.
        /// This will assure that settings such as -WhatIf work properly.
        /// You may call ShouldContinue either before or after ShouldProcess.
        ///
        /// ShouldContinue may only be called during a call to this Cmdlet's
        /// implementation of ProcessRecord, BeginProcessing or EndProcessing,
        /// and only from that thread.
        ///
        /// Cmdlets may have different "classes" of confirmations.  For example,
        /// "del" confirms whether files in a particular directory should be
        /// deleted, whether read-only files should be deleted, etc.
        /// Cmdlets can use ShouldContinue to store YesToAll/NoToAll members
        /// for each such "class" to keep track of whether the user has
        /// confirmed "delete all read-only files" etc.
        /// ShouldProcess offers YesToAll/NoToAll automatically,
        /// but answering YesToAll or NoToAll applies to all subsequent calls
        /// to ShouldProcess for the Cmdlet instance.
        /// </remarks>
        /// <example>
        ///     <snippet Code="C#">
        ///         namespace Microsoft.Samples.MSH.Cmdlet
        ///         {
        ///             [Cmdlet(VerbsCommon.Remove,"myobjecttype4")]
        ///             public class RemoveMyObjectType5 : Cmdlet
        ///             {
        ///                 [Parameter( Mandatory = true )]
        ///                 public string Filename
        ///                 {
        ///                     get { return filename; }
        ///                     set { filename = value; }
        ///                 }
        ///                 private string filename;
        ///
        ///                 [Parameter]
        ///                 public SwitchParameter Force
        ///                 {
        ///                     get { return force; }
        ///                     set { force = value; }
        ///                 }
        ///                 private bool force;
        ///
        ///                 private bool yesToAll;
        ///                 private bool noToAll;
        ///
        ///                 public override void ProcessRecord()
        ///                 {
        ///                     if (ShouldProcess(
        ///                         string.Format("Deleting file {0}",filename),
        ///                         string.Format("Are you sure you want to delete file {0}", filename),
        ///                         "Delete file"))
        ///                     {
        ///                         if (IsReadOnly(filename))
        ///                         {
        ///                             if (!Force &amp;&amp; !ShouldContinue(
        ///                                     string.Format("File {0} is read-only.  Are you sure you want to delete read-only file {0}?", filename),
        ///                                     "Delete file"),
        ///                                     ref yesToAll,
        ///                                     ref noToAll
        ///                                     )
        ///                             {
        ///                                 return;
        ///                             }
        ///                         }
        ///                         // delete the object
        ///                     }
        ///                 }
        ///             }
        ///         }
        ///     </snippet>
        /// </example>
        /// <seealso cref="ShouldContinue(string,string)"/>
        /// <seealso cref="ShouldProcess(string)"/>
        /// <seealso cref="ShouldProcess(string,string)"/>
        /// <seealso cref="ShouldProcess(string,string,string)"/>
        [SuppressMessage("Microsoft.Design", "CA1045:DoNotPassTypesByReference")]
        public bool ShouldContinue(
            string query, string caption, bool hasSecurityImpact, ref bool yesToAll, ref bool noToAll)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                {
                    if (commandRuntime is ICommandRuntime2 runtime2)
                    {
                        return runtime2.ShouldContinue(query, caption, hasSecurityImpact, ref yesToAll, ref noToAll);
                    }
                    else
                    {
                        return commandRuntime.ShouldContinue(query, caption, ref yesToAll, ref noToAll);
                    }
                }
                else
                    return true;
            }
        }

        /// <summary>
        /// Run the cmdlet and get the results as a collection. This is an internal
        /// routine that is used by Invoke to build the underlying collection of
        /// results.
        /// </summary>
        /// <returns>Returns an list of results.</returns>
        internal List<object> GetResults()
        {
            // Prevent invocation of things that derive from PSCmdlet.
            if (this is PSCmdlet)
            {
                string msg = CommandBaseStrings.CannotInvokePSCmdletsDirectly;

                throw new InvalidOperationException(msg);
            }

            var result = new List<object>();
            if (commandRuntime == null)
            {
                CommandRuntime = new DefaultCommandRuntime(result);
            }

            BeginProcessing();
            ProcessRecord();
            EndProcessing();

            return result;
        }
        /// <summary>
        /// Invoke this cmdlet object returning a collection of results.
        /// </summary>
        /// <returns>The results that were produced by this class.</returns>
        public IEnumerable Invoke()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                List<object> data = GetResults();
                for (int i = 0; i < data.Count; i++)
                    yield return data[i];
            }
        }

        /// <summary>
        /// Returns a strongly-typed enumerator for the results of this cmdlet.
        /// </summary>
        /// <typeparam name="T">The type returned by the enumerator</typeparam>
        /// <returns>An instance of the appropriate enumerator.</returns>
        /// <exception cref="InvalidCastException">Thrown when the object returned by the cmdlet cannot be converted to the target type.</exception>
        public IEnumerable<T> Invoke<T>()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                List<object> data = GetResults();
                for (int i = 0; i < data.Count; i++)
                    yield return (T)data[i];
            }
        }

        #endregion ShouldContinue

        #region Transaction Support

        /// <summary>
        /// Returns true if a transaction is available and active.
        /// </summary>
        public bool TransactionAvailable()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (commandRuntime != null)
                    return commandRuntime.TransactionAvailable();
                else
#pragma warning suppress 56503
                    throw new NotImplementedException("TransactionAvailable");
            }
        }

        /// <summary>
        /// Gets an object that surfaces the current PowerShell transaction.
        /// When this object is disposed, PowerShell resets the active transaction.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public PSTransactionContext CurrentPSTransaction
        {
            get
            {
                if (commandRuntime != null)
                    return commandRuntime.CurrentPSTransaction;
                else
                    // We want to throw in this situation, and want to use a
                    // property because it mimics the C# using(TransactionScope ...) syntax
                    throw new NotImplementedException("CurrentPSTransaction");
            }
        }
        #endregion Transaction Support

        #region ThrowTerminatingError
        /// <summary>
        /// Terminate the command and report an error.
        /// </summary>
        /// <param name="errorRecord">
        /// The error which caused the command to be terminated
        /// </param>
        /// <exception cref="PipelineStoppedException">
        /// always
        /// </exception>
        /// <remarks>
        /// <see cref="ThrowTerminatingError"/>
        /// terminates the command, where
        /// <see cref="ICommandRuntime.WriteError"/>
        /// allows the command to continue.
        ///
        /// The cmdlet can also terminate the command by simply throwing
        /// any exception.  When the cmdlet's implementation of
        /// <see cref="ProcessRecord"/>,
        /// <see cref="BeginProcessing"/> or
        /// <see cref="EndProcessing"/>
        /// throws an exception, the Engine will always catch the exception
        /// and report it as a terminating error.
        /// However, it is preferred for the cmdlet to call
        /// <see cref="ThrowTerminatingError"/>,
        /// so that the additional information in
        /// <see cref="ErrorRecord"/>
        /// is available.
        /// <see cref="ThrowTerminatingError"/>
        /// always throws
        /// <see cref="PipelineStoppedException"/>,
        /// regardless of what error was specified in <paramref name="errorRecord"/>.
        /// The Cmdlet should generally just allow
        /// <see cref="PipelineStoppedException"/>.
        /// to percolate up to the caller of
        /// <see cref="ProcessRecord"/>.
        /// etc.
        /// </remarks>
        public void ThrowTerminatingError(ErrorRecord errorRecord)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (errorRecord == null)
                    throw new ArgumentNullException("errorRecord");

                if (commandRuntime != null)
                {
                    commandRuntime.ThrowTerminatingError(errorRecord);
                }
                else if (errorRecord.Exception != null)
                {
                    throw errorRecord.Exception;
                }
                else
                {
                    throw new InvalidOperationException(errorRecord.ToString());
                }
            }
        }
        #endregion ThrowTerminatingError

        #region Exposed API Override

        /// <summary>
        /// When overridden in the derived class, performs initialization
        /// of command execution.
        /// Default implementation in the base class just returns.
        /// </summary>
        /// <exception cref="Exception">
        /// This method is overridden in the implementation of
        /// individual Cmdlets, and can throw literally any exception.
        /// </exception>
        protected virtual void BeginProcessing()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
            }
        }

        /// <summary>
        /// When overridden in the derived class, performs execution
        /// of the command.
        /// </summary>
        /// <exception cref="Exception">
        /// This method is overridden in the implementation of
        /// individual Cmdlets, and can throw literally any exception.
        /// </exception>
        protected virtual void ProcessRecord()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
            }
        }

        /// <summary>
        /// When overridden in the derived class, performs clean-up
        /// after the command execution.
        /// Default implementation in the base class just returns.
        /// </summary>
        /// <exception cref="Exception">
        /// This method is overridden in the implementation of
        /// individual Cmdlets, and can throw literally any exception.
        /// </exception>
        protected virtual void EndProcessing()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
            }
        }

        /// <summary>
        /// When overridden in the derived class, interrupts currently
        /// running code within the command. It should interrupt BeginProcessing,
        /// ProcessRecord, and EndProcessing.
        /// Default implementation in the base class just returns.
        /// </summary>
        /// <exception cref="Exception">
        /// This method is overridden in the implementation of
        /// individual Cmdlets, and can throw literally any exception.
        /// </exception>
        protected virtual void StopProcessing()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
            }
        }

        #endregion Exposed API Override

        #endregion public_methods
    }

    /// <summary>
    /// This describes the reason why ShouldProcess returned what it returned.
    /// Not all possible reasons are covered.
    /// </summary>
    /// <seealso cref="Cmdlet.ShouldProcess(string,string,string,out ShouldProcessReason)"/>
    [Flags]
    public enum ShouldProcessReason
    {
        /// <summary> none of the reasons below </summary>
        None = 0x0,

        /// <summary>
        /// WhatIf behavior was requested.
        /// </summary>
        /// <remarks>
        /// In the MSH host, WhatIf behavior can be requested explicitly
        /// for one cmdlet instance using the -WhatIf commandline parameter,
        /// or implicitly for all SupportsShouldProcess cmdlets with $WhatIfPreference.
        /// Other hosts may have other ways to request WhatIf behavior.
        /// </remarks>
        WhatIf = 0x1,
    }
}
