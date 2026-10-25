(function () {
    function enhance(select) {
        const wrapper = document.createElement("div");
        wrapper.className = "searchable-select position-relative";
        select.parentNode.insertBefore(wrapper, select);
        wrapper.appendChild(select);
        select.classList.add("d-none");

        const input = document.createElement("input");
        input.type = "text";
        input.className = "form-control";
        input.autocomplete = "off";
        input.setAttribute("role", "combobox");
        input.placeholder = select.dataset.placeholder || "Type a name or ID to search";
        select.id && input.setAttribute("aria-label", select.id);
        wrapper.insertBefore(input, select);

        const list = document.createElement("ul");
        list.className = "list-group position-absolute w-100 shadow searchable-select-list d-none";
        list.setAttribute("role", "listbox");
        wrapper.appendChild(list);

        const options = Array.from(select.options)
            .filter(o => o.value !== "")
            .map(o => ({ value: o.value, text: o.text.trim() }));
        let active = -1;
        let shown = [];

        function label(o) { return o.text + " (ID " + o.value + ")"; }

        function syncInput() {
            const o = options.find(x => x.value === select.value);
            input.value = o ? o.text : "";
        }

        function render() {
            const term = input.value.trim().toLowerCase();
            const selected = options.find(x => x.value === select.value);
            const query = selected && input.value === selected.text ? "" : term;
            shown = options.filter(o => !query || o.text.toLowerCase().includes(query) || o.value === query || o.value.startsWith(query));
            list.innerHTML = "";
            shown.forEach((o, i) => {
                const li = document.createElement("li");
                li.className = "list-group-item list-group-item-action" + (o.value === select.value ? " fw-semibold" : "");
                li.textContent = label(o);
                li.setAttribute("role", "option");
                li.addEventListener("mousedown", e => { e.preventDefault(); choose(o); });
                list.appendChild(li);
            });
            if (!shown.length) {
                const li = document.createElement("li");
                li.className = "list-group-item text-muted";
                li.textContent = "No customers found";
                list.appendChild(li);
            }
            active = shown.length ? 0 : -1;
            highlight();
            list.classList.remove("d-none");
        }

        function highlight() {
            Array.from(list.children).forEach((li, i) => {
                li.classList.toggle("active", i === active && shown.length > 0);
                if (i === active) li.scrollIntoView({ block: "nearest" });
            });
        }

        function close() { list.classList.add("d-none"); }

        function choose(o) {
            select.value = o.value;
            input.value = o.text;
            close();
            select.dispatchEvent(new Event("change", { bubbles: true }));
        }

        input.addEventListener("focus", () => { input.select(); render(); });
        input.addEventListener("input", () => {
            if (input.value.trim() === "" && select.value !== "") {
                select.value = "";
                select.dispatchEvent(new Event("change", { bubbles: true }));
            }
            render();
        });
        input.addEventListener("keydown", e => {
            if (e.key === "ArrowDown") { e.preventDefault(); if (list.classList.contains("d-none")) render(); else if (shown.length) { active = (active + 1) % shown.length; highlight(); } }
            else if (e.key === "ArrowUp") { e.preventDefault(); if (shown.length) { active = (active - 1 + shown.length) % shown.length; highlight(); } }
            else if (e.key === "Enter") { if (!list.classList.contains("d-none")) { e.preventDefault(); if (active >= 0) choose(shown[active]); } }
            else if (e.key === "Escape") { close(); syncInput(); }
        });
        input.addEventListener("blur", () => { close(); syncInput(); });
        select.addEventListener("change", syncInput);

        syncInput();
    }

    document.querySelectorAll("select[data-searchable]").forEach(enhance);
})();
