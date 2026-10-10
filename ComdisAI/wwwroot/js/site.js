// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

document.querySelectorAll("[data-role-action-assignment]").forEach((form) => {
    const searchInput = form.querySelector("[data-role-action-search]");
    const selectVisibleInput = form.querySelector("[data-select-visible-actions]");
    const options = Array.from(form.querySelectorAll("[data-role-action-option]"));

    if (!searchInput || !selectVisibleInput || options.length === 0) {
        return;
    }

    const visibleOptions = () => options.filter((option) => !option.hidden);
    const updateSelectVisible = () => {
        const visible = visibleOptions();
        const checkedCount = visible.filter((option) =>
            option.querySelector("[data-role-action-checkbox]").checked).length;

        selectVisibleInput.checked = visible.length > 0 && checkedCount === visible.length;
        selectVisibleInput.indeterminate = checkedCount > 0 && checkedCount < visible.length;
        selectVisibleInput.disabled = visible.length === 0;
    };

    const filterOptions = () => {
        const searchText = searchInput.value.trim().toLocaleLowerCase();

        options.forEach((option) => {
            option.hidden = !option.dataset.searchText.toLocaleLowerCase().includes(searchText);
        });

        updateSelectVisible();
    };

    searchInput.addEventListener("input", filterOptions);
    selectVisibleInput.addEventListener("change", () => {
        visibleOptions().forEach((option) => {
            option.querySelector("[data-role-action-checkbox]").checked = selectVisibleInput.checked;
        });

        updateSelectVisible();
    });
    options.forEach((option) => {
        option.querySelector("[data-role-action-checkbox]").addEventListener("change", updateSelectVisible);
    });

    updateSelectVisible();
});

document.querySelectorAll("[data-user-search]").forEach((searchInput) => {
    const form = searchInput.closest("form");
    const select = form?.querySelector("[data-user-select]");
    const noMatchesMessage = form?.querySelector("[data-user-search-empty]");

    if (!select || !noMatchesMessage) {
        return;
    }

    const options = Array.from(select.querySelectorAll("option")).filter((option) => option.value !== "");

    searchInput.addEventListener("input", () => {
        const searchText = searchInput.value.trim().toLocaleLowerCase();
        let visibleCount = 0;

        options.forEach((option) => {
            const matches = option.dataset.searchText.toLocaleLowerCase().includes(searchText);
            option.hidden = !matches;
            if (matches) {
                visibleCount += 1;
            }
        });

        if (select.selectedOptions[0]?.hidden) {
            select.value = "";
        }

        noMatchesMessage.hidden = visibleCount > 0;
    });
});
