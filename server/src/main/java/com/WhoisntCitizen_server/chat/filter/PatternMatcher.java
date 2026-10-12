package com.WhoisntCitizen_server.chat.filter;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * 여러 단어를 한 번에 찾는 Aho-Corasick 자동자. (토큰 = int)
 * 단어가 수천 개여도 문장을 한 번만 훑어 걸리는 단어를 모두 찾는다.
 * add()로 단어를 다 넣은 뒤 build()를 한 번 부르고, 그 뒤로는 읽기만 하므로 여러 스레드에서 같이 써도 된다.
 */
final class PatternMatcher {

    private static final class Node {
        final Map<Integer, Node> next = new HashMap<>();
        Node fail;
        /** 이 노드에서 끝나는 단어들 {길이, 표시} (fail 링크를 따라 물려받은 것 포함) */
        final List<int[]> outputs = new ArrayList<>();
    }

    private final Node root = new Node();
    private boolean built;
    private int size;

    /** 단어 추가. 빈 단어나 끊김 토큰이 든 단어는 넣지 않는다. flags는 찾을 때 그대로 돌려준다 */
    boolean add(int[] word, int flags) {
        if (built) throw new IllegalStateException("build() 뒤에는 단어를 추가할 수 없습니다.");
        if (word.length == 0) return false;
        for (int code : word) {
            if (code == TextNormalizer.BREAK) return false;
        }
        Node node = root;
        for (int code : word) {
            node = node.next.computeIfAbsent(code, c -> new Node());
        }
        for (int[] out : node.outputs) {
            if (out[0] == word.length) {
                out[1] &= flags; // 같은 단어가 두 번 들어오면 덜 엄격한 쪽(표시가 적은 쪽)을 따른다
                return true;
            }
        }
        node.outputs.add(new int[]{word.length, flags});
        size++;
        return true;
    }

    void build() {
        ArrayDeque<Node> queue = new ArrayDeque<>();
        root.fail = root;
        for (Node child : root.next.values()) {
            child.fail = root;
            queue.add(child);
        }
        while (!queue.isEmpty()) {
            Node node = queue.poll();
            for (Map.Entry<Integer, Node> e : node.next.entrySet()) {
                Node child = e.getValue();
                Node f = node.fail;
                while (f != root && !f.next.containsKey(e.getKey())) f = f.fail;
                Node target = f.next.get(e.getKey());
                child.fail = (target != null && target != child) ? target : root;
                child.outputs.addAll(child.fail.outputs);
                queue.add(child);
            }
        }
        built = true;
    }

    boolean isEmpty() {
        return size == 0;
    }

    /** 걸린 단어들 {토큰 시작, 토큰 끝(포함), 표시} */
    List<int[]> findAll(int[] codes) {
        List<int[]> found = new ArrayList<>();
        if (size == 0) return found;
        Node node = root;
        for (int i = 0; i < codes.length; i++) {
            int code = codes[i];
            while (node != root && !node.next.containsKey(code)) node = node.fail;
            node = node.next.getOrDefault(code, root);
            for (int[] out : node.outputs) {
                found.add(new int[]{i - out[0] + 1, i, out[1]});
            }
        }
        return found;
    }
}
