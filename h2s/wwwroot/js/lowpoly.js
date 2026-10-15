/* Draws the low-poly triangle background as SVG: a jittered grid split into triangles, each filled from colour
   gradients across and down the area. A small dependency-free replacement for Trianglify (which is GPL-licensed). */
window.h2sLowPoly = (() => {
    // Gradients blend in CIE Lab, so midpoints stay vivid rather than going muddy as they do in RGB.
    const white = [0.95047, 1, 1.08883];
    const epsilon = 216 / 24389;
    const kappa = 24389 / 27;

    const hexToRgb = (hex) => {
        const value = parseInt(hex.replace('#', ''), 16);
        return [(value >> 16) & 255, (value >> 8) & 255, value & 255];
    };

    const toLinear = (channel) => {
        const c = channel / 255;
        return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    };

    const fromLinear = (channel) => {
        const c = channel <= 0.0031308 ? 12.92 * channel : 1.055 * channel ** (1 / 2.4) - 0.055;
        return Math.round(Math.min(1, Math.max(0, c)) * 255);
    };

    const labF = (t) => (t > epsilon ? Math.cbrt(t) : (kappa * t + 16) / 116);
    const labFInverse = (t) => (t ** 3 > epsilon ? t ** 3 : (116 * t - 16) / kappa);

    const rgbToLab = (rgb) => {
        const [r, g, b] = rgb.map(toLinear);
        const fx = labF((0.4124 * r + 0.3576 * g + 0.1805 * b) / white[0]);
        const fy = labF((0.2126 * r + 0.7152 * g + 0.0722 * b) / white[1]);
        const fz = labF((0.0193 * r + 0.1192 * g + 0.9505 * b) / white[2]);
        return [116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)];
    };

    const labToRgb = ([l, a, b]) => {
        const fy = (l + 16) / 116;
        const x = labFInverse(fy + a / 500) * white[0];
        const y = labFInverse(fy) * white[1];
        const z = labFInverse(fy - b / 200) * white[2];
        return [
            3.2406 * x - 1.5372 * y - 0.4986 * z,
            -0.9689 * x + 1.8758 * y + 0.0415 * z,
            0.0557 * x - 0.2040 * y + 1.0570 * z
        ].map(fromLinear);
    };

    const mix = (from, to, amount) => from.map((value, i) => value + (to[i] - value) * amount);

    // Returns a function mapping 0..1 to a colour (in Lab) along evenly spaced colour stops.
    const gradient = (colors) => {
        const stops = colors.map((color) => rgbToLab(hexToRgb(color)));
        return (t) => {
            if (stops.length === 1) {
                return stops[0];
            }

            const position = Math.min(1, Math.max(0, t)) * (stops.length - 1);
            const index = Math.min(Math.floor(position), stops.length - 2);
            return mix(stops[index], stops[index + 1], position - index);
        };
    };

    // A repeatable random number in [0, 1) for a grid position: the same seed, row, column and purpose always give the
    // same number, whatever the size of the area.
    const random = (seed, row, column, purpose) => {
        let hash = seed ^ Math.imul(row + 1, 0x9E3779B1) ^ Math.imul(column + 1, 0x85EBCA77) ^ Math.imul(purpose + 1, 0xC2B2AE3D);
        hash = Math.imul(hash ^ (hash >>> 16), 0x7FEB352D);
        hash = Math.imul(hash ^ (hash >>> 15), 0x846CA68B);
        hash ^= hash >>> 16;
        return (hash >>> 0) / 4294967296;
    };

    // Creates the SVG for an area. Points are jittered by up to variance × cellSize, and the grid bleeds one cell past
    // every edge so the jittered triangles still cover the whole area. The grid is anchored at the top left and its
    // randomness comes from the seed, so redrawing a taller or wider area with the same seed only adds triangles: the
    // existing ones stay where they were (though the colour gradients stretch to the new size).
    const createSvg = ({ width, height, cellSize = 75, variance = 0.75, colors, seed = Math.floor(Math.random() * 2 ** 32) }) => {
        const colorAt = gradient(colors);
        const columns = Math.ceil(width / cellSize) + 2;
        const rows = Math.ceil(height / cellSize) + 2;
        const jitter = (row, column, axis) => (random(seed, row, column, axis) - 0.5) * variance * cellSize;

        const points = [];
        for (let row = 0; row <= rows; row++) {
            points.push([]);
            for (let column = 0; column <= columns; column++) {
                points[row].push([(column - 1) * cellSize + jitter(row, column, 0), (row - 1) * cellSize + jitter(row, column, 1)]);
            }
        }

        const svgNamespace = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(svgNamespace, 'svg');
        svg.setAttribute('width', width);
        svg.setAttribute('height', height);

        const addTriangle = (a, b, c) => {
            const centroidX = (a[0] + b[0] + c[0]) / 3;
            const centroidY = (a[1] + b[1] + c[1]) / 3;
            const [red, green, blue] = labToRgb(mix(colorAt(centroidX / width), colorAt(centroidY / height), 0.5));

            const path = document.createElementNS(svgNamespace, 'path');
            path.setAttribute('d', `M${a[0].toFixed(1)},${a[1].toFixed(1)}L${b[0].toFixed(1)},${b[1].toFixed(1)}L${c[0].toFixed(1)},${c[1].toFixed(1)}Z`);
            path.setAttribute('fill', `rgb(${red},${green},${blue})`);
            path.setAttribute('shape-rendering', 'crispEdges');
            svg.appendChild(path);
        };

        // Split each grid cell into two triangles along a randomly chosen diagonal.
        for (let row = 0; row < rows; row++) {
            for (let column = 0; column < columns; column++) {
                const topLeft = points[row][column];
                const topRight = points[row][column + 1];
                const bottomLeft = points[row + 1][column];
                const bottomRight = points[row + 1][column + 1];

                if (random(seed, row, column, 2) < 0.5) {
                    addTriangle(topLeft, topRight, bottomRight);
                    addTriangle(topLeft, bottomRight, bottomLeft);
                } else {
                    addTriangle(topLeft, topRight, bottomLeft);
                    addTriangle(topRight, bottomRight, bottomLeft);
                }
            }
        }

        return svg;
    };

    return { createSvg };
})();
