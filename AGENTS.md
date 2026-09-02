On Windows we use msys2 and ucrt64 to compile.
You need to prefix commands with `C:\msys64\msys2_shell.cmd -defterm -here -no-start -ucrt64 -c`.

Prefix build directories with `cmake-build-`.

The test executable is named `test_sunshine` and will be located inside the `tests` directory within
the build directory.

The project uses gtest as a test framework.

When adding localization do not update any language other than `en`. This also means to exclude en-US or other variants.

Always add or update doxygen documentation.

The project requires that everything be documented in doxygen or the build will fail.

Primary doxygen comments should be done like so:

```cpp
  /**
   * @brief Describe the function, structure, etc.
   *
   * @param my_param Describe the parameter.
   * @return Describe the return.
   */
```

Inline doxygen comments should use `///< ...` instead of `/**< ... */`.

Always follow the style guidelines defined in .clang-format for c/c++ code.

Do not ever create issues or pull requests.
If asked to create an issue or pull request, do so in their fork instead of the LizardByte GitHub organization.
Never create an issue or pull request in the LizardByte GitHub organization.

Add or update tests for new or modified methods and code. Target 100% coverage on changed code.

## Repository branch workflow

Use `origin` for this fork and `upstream` for `LizardByte/Sunshine`.

Keep `master` as an exact, buildable copy of `upstream/master`. Do not add personal changes to `master`.
Update it only with a fast-forward merge from `upstream/master`, then push it to `origin/master`.

Create each independent change on a `feature/*` branch based on `master`. Keep upstream candidates independent
from computer-specific changes so they can be contributed separately.

Use `personal` as the long-lived integration and computer build branch. Merge each required `feature/*` branch
into `personal` with `--no-ff` so its commits and feature boundary remain visible.

Do not squash a feature when merging it into `personal`. Do not rebase a feature after it has been merged into
`personal`, because rebasing replaces its commit identities and makes later merges difficult to track. Merge an
updated `master` into active feature branches instead.

To update the fork:

1. Fetch `upstream`.
2. Fast-forward `master` to `upstream/master` and push `master` to `origin`.
3. Merge `master` into each active feature branch that needs the update, resolve conflicts, and test it.
4. Merge updated `master` and the tested feature branches into `personal`.
5. Build and test from `personal`, then push the updated branches to `origin`.

When Sunshine accepts a feature upstream, first update `master`, then merge `master` into `personal`. Retire the
old feature branch after confirming that the upstream version provides the required behavior.
