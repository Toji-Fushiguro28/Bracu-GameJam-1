// Web build only: ask before the tab closes. Browsers reserve Ctrl+W (close tab) and a page can't block it, but a
// beforeunload handler makes the browser ask "Leave site?" first, so a stray Ctrl+W doesn't lose the day.
mergeInto(LibraryManager.library, {
  LeaveGuard_Enable: function () {
    if (window.whitmoreLeaveGuard) return;
    window.whitmoreLeaveGuard = true;
    window.addEventListener("beforeunload", function (e) {
      e.preventDefault();
      e.returnValue = "";
      return "";
    });
  }
});
