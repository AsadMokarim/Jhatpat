import { Extension } from 'resource:///org/gnome/shell/extensions/extension.js';

export default class JhatpatDonutExtension extends Extension {
    enable() {
        this._windowCreatedId = global.display.connect('window-created', (display, window) => {
            try {
                let title = String(window.get_title() || "");
                let wmClass = String(window.get_wm_class() || "").toLowerCase();
                let isTarget = (title.includes('Donut') || title.includes('Jhatpat') || title.includes('Filee')) ||
                               (wmClass.includes('filee') || wmClass.includes('jhatpat'));
                if (!isTarget || !title.includes('Donut'))
                    return;

                let [x, y] = global.get_pointer();
                let rect = window.get_frame_rect();
                let newX = Math.round(x - rect.width / 2);
                let newY = Math.round(y - rect.height / 2);

                window.move_frame(true, newX, newY);
                window.activate(global.get_current_time());
            } catch (err) {
                console.error(`[JhatpatDonut] Error in window-created: ${err}`);
            }
        });
    }

    disable() {
        if (this._windowCreatedId) {
            global.display.disconnect(this._windowCreatedId);
            this._windowCreatedId = null;
        }
    }
}
