﻿/*
    Copyright (C) 2014-2017 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.ComponentModel;
using System.Diagnostics;
using dnSpy.Contracts.Controls.ToolWindows;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.MVVM;
using dnSpy.Contracts.Text.Classification;
using dnSpy.Debugger.Native;
using dnSpy.Debugger.UI;
using Microsoft.Win32.SafeHandles;

namespace dnSpy.Debugger.ToolWindows.Threads {
	enum ThreadPriority {
		Lowest				= -2,
		BelowNormal			= -1,
		Normal				= 0,
		AboveNormal			= 1,
		Highest				= 2,
	}

	sealed class ThreadVM : ViewModelBase {
		internal bool IsSelectedThread {
			get => isSelectedProcess;
			set {
				Context.UIDispatcher.VerifyAccess();
				if (isSelectedProcess != value) {
					isSelectedProcess = value;
					OnPropertyChanged(nameof(CurrentImageReference));
				}
			}
		}
		bool isSelectedProcess;

		internal bool IsBreakThread {
			get => isBreakThread;
			set {
				Context.UIDispatcher.VerifyAccess();
				if (isBreakThread != value) {
					isBreakThread = value;
					OnPropertyChanged(nameof(CurrentImageReference));
				}
			}
		}
		bool isBreakThread;

		public ImageReference CurrentImageReference {
			get {
				if (IsSelectedThread)
					return DsImages.CurrentInstructionPointer;
				if (IsBreakThread)
					return DsImages.DraggedCurrentInstructionPointer;
				return ImageReference.None;
			}
		}

		public IThreadContext Context { get; }
		public DbgThread Thread { get; }
		public object IdObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowId);
		public object ManagedIdObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowManagedId);
		public object CategoryTextObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowCategoryText);
		public object NameObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowName);
		public object LocationObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowLocation);
		public object PriorityObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowPriority);
		public object AffinityMaskObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowAffinityMask);
		public object SuspendedCountObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowSuspended);
		public object ProcessNameObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowProcess);
		public object AppDomainObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowAppDomain);
		public object StateObject => new FormatterObject<ThreadVM>(this, PredefinedTextClassifierTags.ThreadsWindowUserState);
		internal int Order { get; }

		public ImageReference CategoryImageReference {
			get {
				if (initializeThreadCategory)
					InitializeThreadCategory_UI();
				return categoryImageReference;
			}
		}
		ImageReference categoryImageReference;

		public string CategoryText {
			get {
				if (initializeThreadCategory)
					InitializeThreadCategory_UI();
				return categoryText;
			}
		}
		string categoryText;

		public ThreadPriority Priority {
			get {
				if (hThread == null)
					OpenThread_UI();
				if (priority == null)
					priority = CalculateThreadPriority_UI();
				return priority.Value;
			}
		}
		ThreadPriority? priority;

		public ulong AffinityMask {
			get {
				if (hThread == null)
					OpenThread_UI();
				if (affinityMask == null)
					affinityMask = CalculateAffinityMask_UI();
				return affinityMask.Value;
			}
		}
		ulong? affinityMask;

		public IEditableValue NameEditableValue { get; }
		public IEditValueProvider NameEditValueProvider { get; }

		readonly ThreadCategoryService threadCategoryService;
		bool initializeThreadCategory;
		SafeAccessTokenHandle hThread;

		public ThreadVM(DbgThread thread, IThreadContext context, int order, ThreadCategoryService threadCategoryService, IEditValueProvider nameEditValueProvider) {
			Thread = thread ?? throw new ArgumentNullException(nameof(thread));
			Context = context ?? throw new ArgumentNullException(nameof(context));
			Order = order;
			NameEditValueProvider = nameEditValueProvider ?? throw new ArgumentNullException(nameof(nameEditValueProvider));
			NameEditableValue = new EditableValueImpl(() => Thread.HasName() ? Thread.UIName : string.Empty, s => Thread.UIName = s);
			this.threadCategoryService = threadCategoryService ?? throw new ArgumentNullException(nameof(threadCategoryService));
			initializeThreadCategory = true;
			thread.PropertyChanged += DbgThread_PropertyChanged;
		}

		// UI thread
		ThreadPriority CalculateThreadPriority_UI() {
			Context.UIDispatcher.VerifyAccess();
			Debug.Assert(hThread != null);
			if (hThread == null || hThread.IsInvalid)
				return (ThreadPriority)int.MinValue;
			return (ThreadPriority)NativeMethods.GetThreadPriority(hThread.DangerousGetHandle());
		}

		// UI thread
		ulong CalculateAffinityMask_UI() {
			Context.UIDispatcher.VerifyAccess();
			Debug.Assert(hThread != null);
			if (hThread == null || hThread.IsInvalid)
				return 0;
			var affinityMask = NativeMethods.SetThreadAffinityMask(hThread.DangerousGetHandle(), new IntPtr(-1));
			if (affinityMask != IntPtr.Zero)
				NativeMethods.SetThreadAffinityMask(hThread.DangerousGetHandle(), affinityMask);
			if (IntPtr.Size == 4)
				return (uint)affinityMask.ToInt32();
			return (ulong)affinityMask.ToInt64();
		}

		// UI thread
		void InitializeThreadCategory_UI() {
			Context.UIDispatcher.VerifyAccess();
			initializeThreadCategory = false;
			var info = threadCategoryService.GetInfo(Thread.Kind);
			categoryImageReference = info.Image;
			categoryText = info.Category;
		}

		// UI thread
		internal void RefreshThemeFields_UI() {
			Context.UIDispatcher.VerifyAccess();
			OnPropertyChanged(nameof(IdObject));
			OnPropertyChanged(nameof(ManagedIdObject));
			OnPropertyChanged(nameof(CategoryTextObject));
			OnPropertyChanged(nameof(NameObject));
			OnPropertyChanged(nameof(LocationObject));
			OnPropertyChanged(nameof(PriorityObject));
			OnPropertyChanged(nameof(AffinityMaskObject));
			OnPropertyChanged(nameof(SuspendedCountObject));
			OnPropertyChanged(nameof(ProcessNameObject));
			OnPropertyChanged(nameof(AppDomainObject));
			OnPropertyChanged(nameof(StateObject));
		}

		// UI thread
		internal void RefreshHexFields_UI() {
			Context.UIDispatcher.VerifyAccess();
			OnPropertyChanged(nameof(IdObject));
			OnPropertyChanged(nameof(ManagedIdObject));
			OnPropertyChanged(nameof(LocationObject));
			OnPropertyChanged(nameof(SuspendedCountObject));
		}

		// UI thread
		internal void RefreshAppDomainNames_UI(DbgAppDomain appDomain) {
			Context.UIDispatcher.VerifyAccess();
			if (Thread.AppDomain == appDomain)
				OnPropertyChanged(nameof(AppDomainObject));
		}

		// UI thread
		internal void UpdateFields_UI() {
			Context.UIDispatcher.VerifyAccess();
			if (hThread == null)
				OpenThread_UI();

			var newPriority = CalculateThreadPriority_UI();
			if (newPriority != priority) {
				priority = newPriority;
				OnPropertyChanged(nameof(Priority));
			}

			var newAffinityMask = CalculateAffinityMask_UI();
			if (newAffinityMask != affinityMask) {
				affinityMask = newAffinityMask;
				OnPropertyChanged(nameof(AffinityMask));
			}
		}

		// UI thread
		internal void RefreshEvalFields_UI() {
			Context.UIDispatcher.VerifyAccess();
			//TODO:
		}

		// DbgManager thread
		void DbgThread_PropertyChanged(object sender, PropertyChangedEventArgs e) =>
			Context.UIDispatcher.UI(() => DbgThread_PropertyChanged_UI(e.PropertyName));

		// UI thread
		void DbgThread_PropertyChanged_UI(string propertyName) {
			Context.UIDispatcher.VerifyAccess();
			if (disposed)
				return;
			switch (propertyName) {
			case nameof(DbgThread.AppDomain):
				OnPropertyChanged(nameof(AppDomainObject));
				break;

			case nameof(DbgThread.Kind):
				initializeThreadCategory = true;
				OnPropertyChanged(nameof(CategoryImageReference));
				OnPropertyChanged(nameof(CategoryTextObject));
				break;

			case nameof(DbgThread.Id):
				CloseThreadHandle_UI();
				OnPropertyChanged(nameof(IdObject));
				OnPropertyChanged(nameof(PriorityObject));
				OnPropertyChanged(nameof(AffinityMaskObject));
				break;

			case nameof(DbgThread.ManagedId):
				OnPropertyChanged(nameof(ManagedIdObject));
				break;

			case nameof(DbgThread.Name):
				break;

			case nameof(DbgThread.UIName):
				OnPropertyChanged(nameof(NameObject));
				break;

			case nameof(DbgThread.SuspendedCount):
				OnPropertyChanged(nameof(SuspendedCountObject));
				break;

			case nameof(DbgThread.State):
				OnPropertyChanged(nameof(StateObject));
				break;

			default:
				Debug.Fail($"Unknown thread property: {propertyName}");
				break;
			}
		}

		// UI thread
		void CloseThreadHandle_UI() {
			Context.UIDispatcher.VerifyAccess();
			hThread?.Close();
			hThread = null;
			priority = null;
			affinityMask = null;
		}

		// UI thread
		void OpenThread_UI() {
			Context.UIDispatcher.VerifyAccess();
			if (hThread != null)
				return;
			const int dwDesiredAccess = NativeMethods.THREAD_QUERY_INFORMATION | NativeMethods.THREAD_SET_INFORMATION;
			hThread = NativeMethods.OpenThread(dwDesiredAccess, false, (uint)Thread.Id);
		}

		// UI thread
		internal void ClearEditingValueProperties() {
			Context.UIDispatcher.VerifyAccess();
			NameEditableValue.IsEditingValue = false;
		}

		// UI thread
		internal void Dispose() {
			Context.UIDispatcher.VerifyAccess();
			if (disposed)
				return;
			disposed = true;
			Thread.PropertyChanged -= DbgThread_PropertyChanged;
			CloseThreadHandle_UI();
			ClearEditingValueProperties();
		}
		bool disposed;
	}
}
