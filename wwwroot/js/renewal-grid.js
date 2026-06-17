// ================================================================
// renewal-grid.js — shared renewal pipeline Kendo grid builder
// Used by: AllRenewals.cshtml, RenewalBucket.cshtml
// ================================================================

// Build and return a Kendo Grid for the renewal pipeline.
// readUrl  : the controller action URL for data reads
// maxDays  : passed to BucketRead (null for AllRenewals)
// minDays  : passed to BucketRead (null for AllRenewals)
function buildRenewalGrid(selector, readUrl, maxDays, minDays) {

    var grid = $(selector).kendoGrid({
        dataSource: {
            transport: {
                read: { url: readUrl, type: 'POST' },
                parameterMap: function (data, operation) {
                    var token = $('input[name="__RequestVerificationToken"]').val();
                    if (operation === 'read') {
                        var sf = null, sd = null;
                        if (data.sort && data.sort.length > 0) {
                            sf = data.sort[0].field;
                            sd = data.sort[0].dir;
                        }
                        var params = {
                            page: data.page,
                            pageSize: data.pageSize,
                            sortField: sf,
                            sortDir: sd,
                            searchText: $('#filterSearchText').val(),
                            __RequestVerificationToken: token
                        };
                        if (maxDays !== null && maxDays !== undefined)
                            params.maxDays = maxDays;
                        if (minDays !== null && minDays !== undefined)
                            params.minDays = minDays;
                        return params;
                    }
                    return $.extend({}, data,
                        { __RequestVerificationToken: token });
                }
            },
            schema: {
                data: 'Data', total: 'Total', errors: 'Errors',
                model: {
                    id: 'PolicyId',
                    fields: {
                        PolicyId: { type: 'number' },
                        CoverNoteNumber: { type: 'string' },
                        ClientName: { type: 'string' },
                        ClientId: { type: 'number' },
                        RegistrationNumber: { type: 'string' },
                        PolicyClassName: { type: 'string' },
                        InsurerName: { type: 'string' },
                        ExpiryDate: { type: 'date' },
                        DaysToExpiry: { type: 'number' },
                        NetPremiumPayable: { type: 'number' },
                        RenewalReminderCount: { type: 'number' },
                        PrimaryPhone: { type: 'string' },
                        IsWhatsApp: { type: 'boolean' },
                        AgentCode: { type: 'string' }
                    }
                }
            },
            pageSize: 15,
            serverPaging: true,
            serverSorting: true,
            sort: { field: 'ExpiryDate', dir: 'asc' },
            error: function (e) {
                var msg = 'An error occurred.';
                try {
                    var p = JSON.parse(e.xhr.responseText);
                    if (p.Errors && p.Errors.error && p.Errors.error.errors)
                        msg = p.Errors.error.errors[0];
                } catch (ex) { }
                KSwal.error(msg);
            }
        },
        pageable: { pageSize: 15, pageSizes: [15, 30, 50], info: true },
        sortable: true,
        scrollable: true,
        height: 560,
        columns: [
            {
                // Countdown ring (same Canvas ring as Dashboard Feature 1)
                title: '',
                width: 60,
                sortable: false,
                template: function (item) {
                    var color = item.DaysToExpiry < 30
                        ? '#dc2626'
                        : item.DaysToExpiry <= 60
                            ? '#d97706'
                            : '#16a34a';
                    return '<canvas class="renewal-ring-mini" width="42" height="42" '
                        + 'data-days="' + item.DaysToExpiry + '" '
                        + 'data-color="' + color + '"></canvas>';
                }
            },
            {
                field: 'CoverNoteNumber',
                title: 'Cover Note',
                width: 155,
                template: function (item) {
                    return '<strong>' + kendo.htmlEncode(item.CoverNoteNumber) + '</strong>'
                        + '<br/><small class="text-muted">'
                        + kendo.htmlEncode(item.PolicyClassName) + '</small>';
                }
            },
            {
                field: 'ClientName',
                title: 'Client',
                width: 200,
                template: function (item) {
                    var html = kendo.htmlEncode(item.ClientName);
                    if (item.PrimaryPhone) {
                        html += '<br/><small class="text-muted">'
                            + kendo.htmlEncode(item.PrimaryPhone);
                        if (item.IsWhatsApp) {
                            html += ' <i class="bi bi-whatsapp text-success" '
                                + 'title="WhatsApp reachable"></i>';
                        }
                        html += '</small>';
                    }
                    return html;
                }
            },
            {
                field: 'RegistrationNumber',
                title: 'Vehicle',
                width: 130,
                template: function (item) {
                    return item.RegistrationNumber
                        ? kendo.htmlEncode(item.RegistrationNumber)
                        : '<span class="text-muted">—</span>';
                }
            },
            { field: 'InsurerName', title: 'Insurer', width: 160 },
            {
                field: 'ExpiryDate',
                title: 'Expiry Date',
                width: 130,
                template: function (item) {
                    var d = kendo.toString(kendo.parseDate(item.ExpiryDate), 'dd/MM/yyyy');
                    var color = item.DaysToExpiry < 30
                        ? '#dc2626'
                        : item.DaysToExpiry <= 60 ? '#d97706' : '#16a34a';
                    return d + '<br/><span style="color:' + color
                        + ';font-size:11px;font-weight:700;">'
                        + item.DaysToExpiry + ' days left</span>';
                }
            },
            {
                field: 'NetPremiumPayable',
                title: 'Premium',
                width: 115,
                attributes: { style: 'text-align:right;' },
                template: function (item) {
                    return 'RM ' + parseFloat(item.NetPremiumPayable).toFixed(2);
                }
            },
            {
                field: 'RenewalReminderCount',
                title: 'Reminders',
                width: 100,
                attributes: { style: 'text-align:center;' },
                template: function (item) {
                    if (item.RenewalReminderCount === 0) {
                        return '<span class="text-muted">None</span>';
                    }
                    return '<span class="badge-status badge-pending">'
                        + item.RenewalReminderCount + ' sent</span>';
                }
            },
            {
                title: 'Actions',
                width: 160,
                sortable: false,
                template: function (item) {
                    var html = '';
                    // WhatsApp send button (Feature 6)
                    html += '<button type="button" '
                        + 'class="btn btn-sm btn-outline-success btn-send-reminder me-1" '
                        + 'data-policy-id="' + item.PolicyId + '" '
                        + 'title="Send Reminder">'
                        + '<i class="bi bi-whatsapp"></i></button>';

                    html += '<a href="/Policy/Details/' + item.PolicyId + '" '
                        + 'class="btn btn-sm btn-outline-primary me-1" title="View Policy">'
                        + '<i class="bi bi-eye"></i></a>';

                    html += '<a href="/Policy/RenewPolicy/' + item.PolicyId + '" '
                        + 'class="btn btn-sm btn-outline-secondary" title="Renew">'
                        + '<i class="bi bi-arrow-repeat"></i></a>';

                    return html;
                }
            }
        ]
    }).data("kendoGrid");

    // ---- Draw countdown rings after each data bind ----
    grid.bind('dataBound', function () {
        $('#recordCount').text(this.dataSource.total() + ' records');

        $('.renewal-ring-mini').each(function () {
            var canvas = this;
            var ctx = canvas.getContext('2d');
            var days = parseInt(canvas.getAttribute('data-days'), 10);
            var color = canvas.getAttribute('data-color');
            var cx = canvas.width / 2;
            var cy = canvas.height / 2;
            var r = cx - 4;

            // Fraction: how much of the 90-day window has ELAPSED
            var fraction = Math.max(0, Math.min(1, (90 - days) / 90));

            // Background track
            ctx.beginPath();
            ctx.arc(cx, cy, r, 0, Math.PI * 2);
            ctx.strokeStyle = '#e5e7eb';
            ctx.lineWidth = 4;
            ctx.stroke();

            // Progress arc
            ctx.beginPath();
            ctx.arc(cx, cy, r,
                -Math.PI / 2,
                -Math.PI / 2 + (fraction * Math.PI * 2));
            ctx.strokeStyle = color;
            ctx.lineWidth = 4;
            ctx.lineCap = 'round';
            ctx.stroke();

            // Center label
            ctx.font = 'bold 11px Segoe UI';
            ctx.fillStyle = '#1f2937';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(days, cx, cy);
        });
    });

    return grid;
}


// ================================================================
// bindSearchControls — wires up Search / Clear / Enter key
// ================================================================
function bindSearchControls(grid) {
    $('#btnSearch').on('click', function () { grid.dataSource.page(1); });
    $('#btnClearFilter').on('click', function () {
        $('#filterSearchText').val('');
        grid.dataSource.page(1);
    });
    $('#filterSearchText').on('keypress', function (e) {
        if (e.which === 13) { $('#btnSearch').click(); }
    });
}


// ================================================================
// bindReminderButton — Feature 6 WhatsApp Reminder Simulator
// Fetches a message preview, shows it in KSwal, then logs the send.
// ================================================================
function bindReminderButton(token) {
    $(document).on('click', '.btn-send-reminder', function () {
        var policyId = $(this).data('policy-id');

        // Step 1: Fetch the pre-filled message preview from the server
        $.post('/Renewal/GetReminderPreview',
            { policyId: policyId, __RequestVerificationToken: token })
            .done(function (res) {
                if (!res.success) {
                    KSwal.error(res.Errors
                        ? res.Errors.error.errors[0]
                        : 'Failed to load reminder preview.');
                    return;
                }

                var daysLabel = res.daysLeft + ' day' + (res.daysLeft === 1 ? '' : 's');
                var phoneInfo = res.phoneOrEmail
                    ? res.phoneOrEmail + (res.isWhatsApp ? ' ✅ WhatsApp' : ' (not WhatsApp)')
                    : '⚠️ No primary phone number on file';

                // Step 2: Show WhatsApp message preview + confirm dialog
                Swal.fire({
                    title: '📱 Send WhatsApp Reminder',
                    html:
                        '<div style="text-align:left;">'
                        + '<div class="mb-2">'
                        + '<strong>Recipient:</strong> ' + phoneInfo
                        + '</div>'
                        + '<div class="mb-2">'
                        + '<strong>Notice Type:</strong> ' + res.noticeType
                        + ' (' + daysLabel + ' remaining)'
                        + '</div>'
                        + '<hr/>'
                        + '<div class="mb-2"><strong>Message Preview:</strong></div>'
                        + '<div style="background:#f4f6f9; border-radius:8px; '
                        + 'padding:12px; font-size:13px; white-space:pre-wrap;">'
                        + res.message
                        + '</div>'
                        + '<div class="mt-3">'
                        + '<label style="font-size:13px; font-weight:600;">Agent Note (optional)</label>'
                        + '<input id="swalAgentNote" class="form-control form-control-sm mt-1" '
                        + 'placeholder="e.g. Client called back, will renew next week" />'
                        + '</div>'
                        + '</div>',
                    width: 620,
                    showCancelButton: true,
                    confirmButtonText: '<i class="bi bi-whatsapp"></i> Send Reminder',
                    cancelButtonText: 'Cancel',
                    confirmButtonColor: '#16a34a',
                    cancelButtonColor: '#94a3b8',
                    didOpen: function () {
                        // Stop Swal from closing when clicking inside
                        document.getElementById('swalAgentNote')
                            .addEventListener('click', function (e) { e.stopPropagation(); });
                    }
                }).then(function (result) {
                    if (!result.isConfirmed) return;

                    var agentNote = document.getElementById('swalAgentNote')?.value || '';

                    // Step 3: POST the confirmed send to the server (logs to RenewalNotices)
                    $.post('/Renewal/SendReminder', {
                        PolicyId: policyId,
                        NoticeType: res.noticeType,
                        Channel: 'WhatsApp',
                        PhoneOrEmail: res.phoneOrEmail,
                        MessageContent: res.message,
                        AgentNote: agentNote,
                        __RequestVerificationToken: token
                    })
                        .done(function (sendRes) {
                            if (sendRes.success) {
                                KSwal.success(sendRes.message);
                                // Refresh the grid so RenewalReminderCount updates
                                if (typeof grid !== 'undefined') {
                                    grid.dataSource.read();
                                }
                            } else {
                                KSwal.error('Failed to log reminder.');
                            }
                        });
                });
            })
            .fail(function () {
                KSwal.error('Could not load reminder preview. Please try again.');
            });
    });
}