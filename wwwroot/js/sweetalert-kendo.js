/**
 * sweetalert-kendo.js  — PTC IGH System
 * ═══════════════════════════════════════════════════════════════
 * Single global: KSwal
 * Fixes SweetAlert2 z-index with KendoUI 2018 + Bootstrap 5
 * All popups appear TRUE CENTER of viewport
 *
 * USAGE:
 *   KSwal.toast('success'|'error'|'warning'|'info', 'Message')
 *   KSwal.success('Title', 'Message')
 *   KSwal.error('Title', 'Message')
 *   KSwal.warning('Title', 'Message')
 *   KSwal.info('Title', 'Message')
 *   KSwal.confirm('Title', 'Message', onConfirm, onCancel?)
 *   KSwal.confirmDelete('Title', 'Message', onConfirm)
 *   KSwal.loading('Please wait...')  then  KSwal.close()
 *   KSwal.fire({ title:'X', html:'Y', icon:'success' })
 * ═══════════════════════════════════════════════════════════════
 */
var KSwal = (function () {
    'use strict';

    var Z_ACTIVE  = 20000;
    var Z_DEFAULT = 1060;

    /* Inject CSS once — single @ is correct in a plain .js file */
    (function injectCSS() {
        if (document.getElementById('kswal-css')) return;
        var css = [
            '.swal2-container{position:fixed!important;top:0!important;left:0!important;',
            'right:0!important;bottom:0!important;width:100vw!important;height:100vh!important;',
            'display:flex!important;align-items:center!important;justify-content:center!important;',
            'margin:0!important;padding:20px!important;z-index:' + Z_DEFAULT + '!important;}',

            '.swal2-popup{position:relative!important;margin:auto!important;',
            'width:440px!important;max-width:calc(100vw - 40px)!important;',
            'border-radius:14px!important;font-family:"DM Sans",sans-serif!important;',
            'box-shadow:0 24px 60px rgba(0,0,0,0.25)!important;}',

            '.swal2-popup.swal2-toast{width:auto!important;min-width:260px!important;',
            'max-width:calc(100vw - 40px)!important;border-radius:10px!important;}',

            '.swal2-title{font-family:"Sora",sans-serif!important;font-size:1.15rem!important;',
            'color:#1e293b!important;padding-top:6px!important;}',

            '.swal2-html-container{font-size:0.875rem!important;color:#475569!important;',
            'line-height:1.65!important;}',

            '.swal2-actions{gap:10px!important;margin-top:18px!important;}',

            '.swal2-confirm,.swal2-cancel,.swal2-deny{border-radius:8px!important;',
            'font-family:"DM Sans",sans-serif!important;font-weight:600!important;',
            'font-size:0.875rem!important;padding:9px 22px!important;min-width:90px!important;}'
        ].join('');
        var tag = document.createElement('style');
        tag.id  = 'kswal-css';
        tag.innerHTML = css;
        document.head.appendChild(tag);
    })();

    /* Push Kendo windows + Bootstrap modals behind SweetAlert */
    function _suppress() {
        var saved = [];
        if (typeof $ === 'undefined') return saved;
        $('.k-window').each(function () {
            var $w = $(this);
            saved.push({ el: $w, z: $w.css('z-index') });
            $w.css('z-index', Z_DEFAULT - 1);
        });
        $('.modal.show').each(function () {
            var $m = $(this), $b = $('.modal-backdrop');
            saved.push({ el: $m, z: $m.css('z-index') });
            saved.push({ el: $b, z: $b.css('z-index') });
            $m.css('z-index', Z_DEFAULT - 2);
            $b.css('z-index', Z_DEFAULT - 3);
        });
        return saved;
    }

    function _restore(saved) {
        if (!saved || !saved.length) return;
        $.each(saved, function (i, item) {
            if (item.el && item.el.length) item.el.css('z-index', item.z);
        });
    }

    /* Force the swal2-container to sit above everything */
    function _elevate(popup) {
        var c = null;
        if (popup && popup.closest) c = popup.closest('.swal2-container');
        if (!c && typeof $ !== 'undefined') c = $('.swal2-container')[0];
        if (c) {
            c.style.setProperty('z-index',  String(Z_ACTIVE), 'important');
            c.style.setProperty('position', 'fixed',          'important');
            c.style.setProperty('top',      '0',              'important');
            c.style.setProperty('left',     '0',              'important');
        }
    }

    /* Reset z-index after close */
    function _afterClose(saved, cb) {
        _restore(saved);
        if (typeof $ !== 'undefined' && !Swal.isVisible()) {
            $('.swal2-container').css('z-index', Z_DEFAULT);
        }
        if (typeof cb === 'function') cb();
    }

    /* ── Core fire() ── */
    function fire(options, text, icon) {
        if (typeof options === 'string') {
            options = { title: options, text: text || '', icon: icon || 'info' };
        }
        var saved = null;
        var owp   = options.willOpen;
        var odc   = options.didClose;

        return Swal.fire($.extend({}, options, {
            willOpen: function (p) { saved = _suppress(); _elevate(p); if (owp) owp(p); },
            didClose: function ()  { _afterClose(saved, odc); saved = null; }
        }));
    }

    /* ── toast() ── */
    function toast(icon, message, timer) {
        var saved = _suppress();
        return Swal.mixin({
            toast: true,
            position: 'top-end',
            showConfirmButton: false,
            timer: timer || 3200, timerProgressBar: true,
            didOpen:  function (t) { _elevate(t); t.addEventListener('mouseenter', Swal.stopTimer); t.addEventListener('mouseleave', Swal.resumeTimer); },
            didClose: function ()  { _afterClose(saved, null); saved = null; }
        }).fire({ icon: icon, title: message });
    }

    /* ── alert() ────────────────────────────────────────────── */
    function alert(message, title, icon) {
        return fire({
            title: title || 'Notice',
            html: message || '',
            icon: icon || 'info',
            confirmButtonText: 'OK',
            confirmButtonColor: '#1a6eb5'
        });
    }

    /* ── success() ── */
    function success(title, message) {
        return fire({ title: title || 'Success', html: message || '', icon: 'success', confirmButtonText: 'OK', confirmButtonColor: '#059669' });
    }

    /* ── error() ── */
    function error(title, message) {
        return fire({ title: title || 'Error', html: message || '', icon: 'error', confirmButtonText: 'OK', confirmButtonColor: '#dc2626' });
    }

    /* ── warning() ── */
    function warning(title, message) {
        return fire({ title: title || 'Warning', html: message || '', icon: 'warning', confirmButtonText: 'OK', confirmButtonColor: '#d97706' });
    }

    /* ── info() ── */
    function info(title, message) {
        return fire({ title: title || 'Information', html: message || '', icon: 'info', confirmButtonText: 'OK', confirmButtonColor: '#1a6eb5' });
    }

    /* ── confirm() ── */
    function confirm(title, message, onConfirm, onCancel) {
        return fire({
            title: title || 'Are you sure?', html: message || '', icon: 'question',
            showCancelButton: true, confirmButtonText: 'Confirm', cancelButtonText: 'Cancel',
            confirmButtonColor: '#1a6eb5', cancelButtonColor: '#94a3b8', reverseButtons: true
        }).then(function (r) {
            if (r.isConfirmed && typeof onConfirm === 'function') onConfirm();
            if (r.isDismissed && typeof onCancel  === 'function') onCancel();
        });
    }

    /* ── confirmDelete() ── */
    function confirmDelete(title, message, onConfirm) {
        return fire({
            title: title || 'Delete?', html: message || 'This action cannot be undone.', icon: 'warning',
            showCancelButton: true, confirmButtonText: 'Delete', cancelButtonText: 'Cancel',
            confirmButtonColor: '#dc2626', cancelButtonColor: '#94a3b8', reverseButtons: true
        }).then(function (r) {
            if (r.isConfirmed && typeof onConfirm === 'function') onConfirm();
        });
    }

    /* ── loading() ── */
    function loading(message) {
        var saved = _suppress();
        Swal.fire({
            title: message || 'Please wait...', allowOutsideClick: false,
            allowEscapeKey: false, showConfirmButton: false,
            willOpen:  function (p) { Swal.showLoading(); _elevate(p); },
            didClose:  function ()  { _afterClose(saved, null); saved = null; }
        });
    }

    /* ── close() ── */
    function close() { Swal.close(); }

    return { fire: fire, toast: toast, alert: alert, success: success, error: error, warning: warning, info: info, confirm: confirm, confirmDelete: confirmDelete, loading: loading, close: close };

}());
