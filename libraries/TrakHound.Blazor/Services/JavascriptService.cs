// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.JSInterop;
using TrakHound.Apps;

namespace TrakHound.Blazor.Services
{
    public class JavascriptService : ITrakHoundTransientAppInjectionService
    {
        private readonly IJSRuntime _jsRuntime;


        public JavascriptService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }


        public async Task InvokeVoid(string functionName, params object[] args)
        {
            if (!string.IsNullOrEmpty(functionName))
            {
                try
                {
                    await _jsRuntime.InvokeVoidAsync(functionName, args);
                }
                catch (Exception) 
                {
                    // Need to Log Exceptions
                }
            }
        }

		public async Task<T> Invoke<T>(string functionName, params object[] args)
		{
			if (!string.IsNullOrEmpty(functionName))
			{
				try
				{
					return await _jsRuntime.InvokeAsync<T>(functionName, args);
				}
				catch (Exception)
				{
					// Need to Log Exceptions
				}
			}

            return default;
		}


        public async Task Reload()
        {
            await InvokeVoid("JsFunctions.reload");
        }

        public async Task FocusElement(string elementId)
        {
            await InvokeVoid("JsFunctions.focusElement", elementId);
        }

        public async Task FocusSelectElement(string elementId)
        {
            await InvokeVoid("JsFunctions.focusSelectElement", elementId);
        }

        public async Task ClickElement(string elementId)
        {
            await InvokeVoid("JsFunctions.clickElement", elementId);
        }

        public async Task SelectElement(string elementId)
        {
            await InvokeVoid("JsFunctions.selectElement", elementId);
        }

        public async Task CopyClipboard(string text)
        {
            await InvokeVoid("JsFunctions.copyClipboard", text);
        }

        public async Task<double> GetElementWidth(string elementId)
		{
			return await Invoke<double>("JsFunctions.getElementWidth", elementId);
		}

        public async Task ScrollIntoView(string elementId)
        {
            await InvokeVoid("JsFunctions.scrollIntoView", elementId);
        }

        /// <summary>
        /// Returns whether the scroll container is at (or within 'threshold' pixels of) the bottom. Returns true if it cannot be determined.
        /// </summary>
        public async Task<bool> IsScrolledToBottom(Microsoft.AspNetCore.Components.ElementReference element, int threshold = 0)
        {
            return await Invoke<bool?>("JsFunctions.isScrolledToBottom", element, threshold) ?? true;
        }

        /// <summary>
        /// Scrolls the scroll container to the bottom (without scrolling any of its ancestors)
        /// </summary>
        public async Task ScrollToBottom(Microsoft.AspNetCore.Components.ElementReference element)
        {
            await InvokeVoid("JsFunctions.scrollToBottom", element);
        }

        /// <summary>
        /// Lets a handle on a sidebar's edge change the sidebar's width. The width is set as the CSS variable 'variable' on the container
        /// (whose layout uses it) and kept in Local Storage under 'storageKey'. 'side' is the side of the container the sidebar is on (left or right)
        /// </summary>
        public async Task AttachResizeHandle(Microsoft.AspNetCore.Components.ElementReference handle, Microsoft.AspNetCore.Components.ElementReference container, Microsoft.AspNetCore.Components.ElementReference sidebar, string variable, string storageKey, string side, int min, int minRemaining)
        {
            await InvokeVoid("JsFunctions.attachResizeHandle", handle, container, sidebar, new { variable, storageKey, side, min, minRemaining });
        }

        public async Task StartTimer(string timerId, string elementId)
        {
            await InvokeVoid("JsFunctions.startTimer", timerId, elementId);
        }

        public async Task StopTimer(string timerId)
        {
            await InvokeVoid("JsFunctions.stopTimer", timerId);
        }
    }
}
